using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.Xbox360.Exceptions;

namespace JoySmoother;

public partial class MainWindow : Window
{
    private const int XInputSlotCount = 4;
    private const int PollIntervalMilliseconds = 8;
    private const int DisplayIntervalMilliseconds = 50;
    private const int MinimumDriftZone = 0;
    private const int MaximumDriftZone = 10000;
    private const int MinimumVelocityCutoff = 0;
    private const int MaximumVelocityCutoff = 1500;
    private const int MinimumSettleDelayFrames = 1;
    private const int MaximumSettleDelayFrames = 60;
    private const int DefaultDriftZone = 2621;
    private const int DefaultVelocityCutoff = 80;
    private const int DefaultSettleDelayFrames = 8;

    private readonly DispatcherTimer _pollTimer;
    private readonly InputReading[] _latestReadings = new InputReading[XInputSlotCount];

    private ViGEmClient? _virtualControllerClient;
    private IXbox360Controller? _virtualController;
    private short _previousX;
    private short _previousY;
    private int _stationaryFrames;
    private long _lastDisplayUpdate;
    private int _driftZone = DefaultDriftZone;
    private int _velocityCutoff = DefaultVelocityCutoff;
    private int _settleDelayFrames = DefaultSettleDelayFrames;
    private bool? _lastReportedInputConnection;
    private bool _xInputAvailable = true;
    private bool _isRunning;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        for (int slot = 0; slot < XInputSlotCount; slot++)
        {
            ControllerSlots.Add(new ControllerSlotViewModel(slot));
        }

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(PollIntervalMilliseconds)
        };
        _pollTimer.Tick += PollControllers;
        _pollTimer.Start();
    }

    public ObservableCollection<ControllerSlotViewModel> ControllerSlots { get; } = new();

    private int SelectedSlot => Math.Clamp(ControllerSelector.SelectedIndex, 0, XInputSlotCount - 1);

    private void PollControllers(object? sender, EventArgs e)
    {
        try
        {
            for (int slot = 0; slot < XInputSlotCount; slot++)
            {
                bool connected = TryReadController((uint)slot, out short x, out short y);
                _latestReadings[slot] = new InputReading(connected, x, y);
                ControllerSlots[slot].Update(connected, x, y);
            }
        }
        catch (DllNotFoundException)
        {
            HandleXInputUnavailable();
            return;
        }
        catch (EntryPointNotFoundException)
        {
            HandleXInputUnavailable();
            return;
        }

        InputReading reading = _latestReadings[SelectedSlot];
        SmoothedStickState smoothed = Smooth(reading);

        if (_isRunning && _virtualController is not null)
        {
            try
            {
                _virtualController.SetAxisValue(Xbox360Axis.LeftThumbX, smoothed.X);
                _virtualController.SetAxisValue(Xbox360Axis.LeftThumbY, smoothed.Y);
            }
            catch (Exception exception) when (IsKnownViGEmFailure(exception))
            {
                StopSmoothing();
                SetStatus($"Virtual controller communication failed: {exception.Message}", isError: true);
                return;
            }
        }

        if (_isRunning && _lastReportedInputConnection != reading.Connected)
        {
            SetStatus(reading.Connected
                ? "Input controller connected. Filtered output is active."
                : "Input controller disconnected. Virtual stick output is neutral until it reconnects.",
                isError: !reading.Connected);
            _lastReportedInputConnection = reading.Connected;
        }

        if (Environment.TickCount64 - _lastDisplayUpdate >= DisplayIntervalMilliseconds)
        {
            RawValuesText.Text = $"X: {reading.X,6}    Y: {reading.Y,6}";
            SmoothedValuesText.Text = $"X: {smoothed.X,6}    Y: {smoothed.Y,6}";
            _lastDisplayUpdate = Environment.TickCount64;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            return;
        }

        int selectedSlot = SelectedSlot;
        try
        {
            bool connected = TryReadController((uint)selectedSlot, out short x, out short y);
            _latestReadings[selectedSlot] = new InputReading(connected, x, y);
            ControllerSlots[selectedSlot].Update(connected, x, y);
        }
        catch (DllNotFoundException)
        {
            HandleXInputUnavailable();
            return;
        }
        catch (EntryPointNotFoundException)
        {
            HandleXInputUnavailable();
            return;
        }

        if (!_latestReadings[selectedSlot].Connected)
        {
            SetStatus("The selected XInput slot is disconnected. Connect a controller or select another slot.", isError: true);
            return;
        }

        bool virtualMode = OutputModeSelector.SelectedIndex == 1;
        if (!TryReadParameterInputs(out string validationMessage))
        {
            SetStatus(validationMessage, isError: true);
            return;
        }

        if (virtualMode)
        {
            try
            {
                _virtualControllerClient = new ViGEmClient();
                _virtualController = _virtualControllerClient.CreateXbox360Controller();
                _virtualController.Connect();
            }
            catch (Exception exception) when (IsKnownViGEmFailure(exception))
            {
                StopVirtualController();
                SetStatus(GetVirtualControllerStartupMessage(exception), isError: true);
                return;
            }
        }

        ResetFilter();
        _isRunning = true;
        _lastReportedInputConnection = _latestReadings[selectedSlot].Connected;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ControllerSelector.IsEnabled = false;
        OutputModeSelector.IsEnabled = false;

        SetStatus(virtualMode
            ? "Running. Filtered input is being sent to the virtual Xbox controller."
            : "Monitoring. Game input is not being modified.");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopSmoothing();
        SetStatus("Stopped. Controller readings remain available for identification.");
    }

    private void StopSmoothing()
    {
        _isRunning = false;
        _lastReportedInputConnection = null;
        StopVirtualController();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ControllerSelector.IsEnabled = true;
        OutputModeSelector.IsEnabled = true;
        ResetFilter();
    }

    private void StopVirtualController()
    {
        _virtualController = null;
        _virtualControllerClient?.Dispose();
        _virtualControllerClient = null;
    }

    private void ControllerSelection_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ResetFilter();
    }

    private void ParameterInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (DriftZoneInput is null ||
            VelocityCutoffInput is null ||
            SettleDelayInput is null ||
            StartButton is null ||
            StatusText is null)
        {
            return;
        }

        bool isValid = TryReadParameterInputs(out string validationMessage);
        SetParameterInputState(DriftZoneInput, isValid || IsInRange(DriftZoneInput.Text, MinimumDriftZone, MaximumDriftZone));
        SetParameterInputState(VelocityCutoffInput, isValid || IsInRange(VelocityCutoffInput.Text, MinimumVelocityCutoff, MaximumVelocityCutoff));
        SetParameterInputState(SettleDelayInput, isValid || IsInRange(SettleDelayInput.Text, MinimumSettleDelayFrames, MaximumSettleDelayFrames));

        if (!isValid)
        {
            if (!_isRunning)
            {
                StartButton.IsEnabled = false;
            }

            SetStatus(_isRunning
                ? $"{validationMessage} The last valid values remain active."
                : validationMessage, isError: true);
        }
        else
        {
            if (!_isRunning)
            {
                StartButton.IsEnabled = _xInputAvailable;
                if (_xInputAvailable)
                {
                    SetStatus("Ready. Valid parameter values are applied immediately.");
                }
            }
            else
            {
                SetStatus(OutputModeSelector.SelectedIndex == 1
                    ? "Running. Filtered input is being sent to the virtual Xbox controller."
                    : "Monitoring. Game input is not being modified.");
            }
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(89, 101, 121));
    }

    private SmoothedStickState Smooth(InputReading reading)
    {
        short x = reading.Connected ? reading.X : (short)0;
        short y = reading.Connected ? reading.Y : (short)0;

        double distanceFromCenter = Math.Sqrt((double)x * x + (double)y * y);
        double deltaX = x - _previousX;
        double deltaY = y - _previousY;
        double velocity = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

        short outputX = x;
        short outputY = y;

        if (reading.Connected &&
            distanceFromCenter < _driftZone &&
            velocity < _velocityCutoff)
        {
            _stationaryFrames++;
            if (_stationaryFrames >= _settleDelayFrames)
            {
                outputX = 0;
                outputY = 0;
            }
        }
        else
        {
            _stationaryFrames = 0;
        }

        _previousX = x;
        _previousY = y;

        return new SmoothedStickState(outputX, outputY);
    }

    private bool TryReadParameterInputs(out string validationMessage)
    {
        if (!TryParseInRange(DriftZoneInput.Text, MinimumDriftZone, MaximumDriftZone, out int driftZone))
        {
            validationMessage = "Drift zone must be a whole number from 0 to 10000.";
            return false;
        }

        if (!TryParseInRange(VelocityCutoffInput.Text, MinimumVelocityCutoff, MaximumVelocityCutoff, out int velocityCutoff))
        {
            validationMessage = "Velocity cutoff must be a whole number from 0 to 1500.";
            return false;
        }

        if (!TryParseInRange(SettleDelayInput.Text, MinimumSettleDelayFrames, MaximumSettleDelayFrames, out int settleDelayFrames))
        {
            validationMessage = "Settle delay must be a whole number from 1 to 60.";
            return false;
        }

        bool parametersChanged =
            _driftZone != driftZone ||
            _velocityCutoff != velocityCutoff ||
            _settleDelayFrames != settleDelayFrames;

        _driftZone = driftZone;
        _velocityCutoff = velocityCutoff;
        _settleDelayFrames = settleDelayFrames;
        if (parametersChanged)
        {
            ResetFilter();
        }

        validationMessage = string.Empty;
        return true;
    }

    private static bool IsInRange(string text, int minimum, int maximum) =>
        TryParseInRange(text, minimum, maximum, out _);

    private static bool TryParseInRange(string text, int minimum, int maximum, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
        value >= minimum &&
        value <= maximum;

    private static void SetParameterInputState(System.Windows.Controls.TextBox input, bool isValid)
    {
        input.BorderBrush = isValid ? SystemColors.ControlDarkBrush : Brushes.Firebrick;
        input.ToolTip = isValid ? null : "Enter a whole number within the stated range.";
    }

    private void ResetFilter()
    {
        _previousX = 0;
        _previousY = 0;
        _stationaryFrames = 0;
    }

    private static bool TryReadController(uint userIndex, out short x, out short y)
    {
        if (XInputGetState(userIndex, out XInputState state) == 0)
        {
            x = state.Gamepad.LeftThumbX;
            y = state.Gamepad.LeftThumbY;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private void HandleXInputUnavailable()
    {
        _xInputAvailable = false;
        _pollTimer.Stop();
        StopSmoothing();
        SetStatus("XInput is not available on this Windows installation.", isError: true);
        StartButton.IsEnabled = false;
    }

    private static bool IsKnownViGEmFailure(Exception exception) =>
        exception is VigemBusNotFoundException or
            VigemBusAccessFailedException or
            VigemBusInvalidHandleException or
            VigemBusVersionMismatchException or
            VigemNoFreeSlotException or
            VigemAllocFailedException or
            VigemAlreadyConnectedException or
            VigemInvalidTargetException or
            VigemInvalidParameterException or
            VigemCallbackAlreadyRegisteredException or
            VigemCallbackNotFoundException or
            VigemIsDisposingException or
            VigemNotSupportedException or
            VigemRemovalFailedException or
            VigemTargetNotPluggedInException or
            VigemTargetUninitializedException or
            Xbox360UserIndexNotReportedException;

    private static string GetVirtualControllerStartupMessage(Exception exception) =>
        exception is VigemBusNotFoundException
            ? "ViGEmBus was not found. Install the signed ViGEmBus driver, then restart Windows."
            : $"Could not start the virtual controller: {exception.Message}";

    protected override void OnClosing(CancelEventArgs e)
    {
        _pollTimer.Stop();
        StopSmoothing();
        base.OnClosing(e);
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    private readonly record struct InputReading(bool Connected, short X, short Y);

    private readonly record struct SmoothedStickState(short X, short Y);
}

public sealed class ControllerSlotViewModel : INotifyPropertyChanged
{
    private bool _connected;
    private short _x;
    private short _y;

    public ControllerSlotViewModel(int slot)
    {
        Slot = slot;
        SlotName = $"XInput slot {slot}";
        Update(false, 0, 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Slot { get; }
    public string SlotName { get; }
    public string DisplayName => $"{SlotName} — {(_connected ? "connected" : "disconnected")}";
    public string ConnectionText => _connected ? "Connected" : "Disconnected";
    public Brush ConnectionColor => _connected ? Brushes.ForestGreen : Brushes.Gray;
    public string AxisText => $"X {_x,6}  Y {_y,6}";

    public void Update(bool connected, short x, short y)
    {
        if (_connected == connected && _x == x && _y == y)
        {
            return;
        }

        _connected = connected;
        _x = connected ? x : (short)0;
        _y = connected ? y : (short)0;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectionText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectionColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AxisText)));
    }
}
