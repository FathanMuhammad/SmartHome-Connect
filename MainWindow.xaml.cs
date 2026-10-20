using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartHomeAcController
{
    public partial class MainWindow : Window
    {
        private readonly AppConfig _config;
        private readonly TuyaClient _tuyaClient;
        private readonly WizLampClient _wizClient;

        // AC State
        private int _currentPower = 1;
        private int _currentTemp = 22;
        private int _currentMode = 0; // 0=Cool
        private int _currentWind = 0; // 0=Auto, 1=Low, 2=Med, 3=High
        private bool _isUpdatingUi = false;
        private bool _isInitialized = false;

        // WiZ Lamps State
        private bool _isSyncingLampUi = false;
        private WizLampDevice _lampuMeja = new() { Mac = "A8BB50B5A2E9", Name = "Lampu Meja", IpAddress = "192.168.1.2" };
        private WizLampDevice _lampuLedStrip = new() { Mac = "CC40858E7E82", Name = "Lampu LED Strip", IpAddress = "192.168.1.4" };

        // Debounce tokens for smooth slider interactions
        private CancellationTokenSource? _mejaBrightCts;
        private CancellationTokenSource? _mejaKelvinCts;
        private CancellationTokenSource? _stripBrightCts;
        private CancellationTokenSource? _stripKelvinCts;

        public MainWindow()
        {
            // Initialize config and clients before XAML components load
            _config = AppConfig.Load();
            _tuyaClient = new TuyaClient(_config);
            _tuyaClient.OnLog += AppendLog;

            _wizClient = new WizLampClient();
            _wizClient.OnLog += AppendLog;

            InitializeComponent();

            InitializeUiState();
            _isInitialized = true;

            // Trigger non-blocking lamp status sync & discovery on start
            _ = InitialSyncLampsAsync();
        }

        private void InitializeUiState()
        {
            _isUpdatingUi = true;

            // Load credentials & connection
            ClientSecretBox.Password = _config.ClientSecret;
            ClientSecretVisibleBox.Text = _config.ClientSecret;
            InfraredIdBox.Text = _config.InfraredId;
            RemoteIdBox.Text = _config.RemoteId;
            SwapCheckBox.IsChecked = _config.IsSwapped;
            UpdateSwapLabels(_config.IsSwapped);

            // Select matching data center
            foreach (ComboBoxItem item in DataCenterComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.BaseUrl)
                {
                    DataCenterComboBox.SelectedItem = item;
                    break;
                }
            }

            DataCenterComboBox.SelectionChanged += OnConfigInputChanged;

            // Restore AC states
            _currentPower = _config.LastPower;
            _currentTemp = Math.Clamp(_config.LastTemp, 16, 30);
            _currentMode = Math.Clamp(_config.LastMode, 0, 4);
            _currentWind = Math.Clamp(_config.LastWind, 0, 3);

            UpdateTempDisplay();

            // Set Wind RadioButton
            foreach (RadioButton rb in WindGrid.Children.OfType<RadioButton>())
            {
                if (int.TryParse(rb.Tag?.ToString(), out int tagVal) && tagVal == _currentWind)
                {
                    rb.IsChecked = true;
                    break;
                }
            }

            // Restore cached Lamp IPs if available, otherwise keep verified defaults
            if (!string.IsNullOrWhiteSpace(_config.LampuMejaIp))
            {
                _lampuMeja.IpAddress = _config.LampuMejaIp;
            }
            if (!string.IsNullOrWhiteSpace(_config.LampuLedStripIp))
            {
                _lampuLedStrip.IpAddress = _config.LampuLedStripIp;
            }

            LampuMejaIpText.Text = $"({_lampuMeja.IpAddress})";
            LampuLedStripIpText.Text = $"({_lampuLedStrip.IpAddress})";

            _isUpdatingUi = false;
            UpdateSummaryText();

            AppendLog($"Aplikasi siap. Terhubung ke Tuya ({_config.BaseUrl}) & WiZ Lamp.", false);
            SetStatus("Siap", "#10B981");
        }

        private void AppendLog(string message, bool isError)
        {
            Dispatcher.InvokeAsync(() =>
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.ScrollToEnd();

                if (isError)
                {
                    LogTextBox.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                }
                else
                {
                    LogTextBox.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                }
            });
        }

        private void SetStatus(string text, string hexColor)
        {
            Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = text;
                StatusDot.Fill = (SolidColorBrush)new BrushConverter().ConvertFrom(hexColor)!;
            });
        }

        // ==========================================
        // TAB NAVIGATION
        // ==========================================
        private void TabNav_Click(object sender, RoutedEventArgs e)
        {
            if (TabAcBtn.IsChecked == true)
            {
                AcViewPanel.Visibility = Visibility.Visible;
                LampViewPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                AcViewPanel.Visibility = Visibility.Collapsed;
                LampViewPanel.Visibility = Visibility.Visible;
            }
        }

        // ==========================================
        // AC KONTROL
        // ==========================================
        private void UpdateSummaryText()
        {
            string windName = _currentWind switch
            {
                0 => "Fan Auto",
                1 => "Fan Low",
                2 => "Fan Med",
                3 => "Fan High",
                _ => "Auto"
            };

            string powerStatus = _currentPower == 1 ? "ON" : "OFF";
            CurrentStateSummary.Text = $"{powerStatus} • Cool • {windName}";
        }

        private void UpdateTempDisplay()
        {
            TempDisplay.Text = $"{_currentTemp}°";
            UpdateSummaryText();
        }

        private void SaveCurrentConfig()
        {
            if (!_isInitialized || _isUpdatingUi || _config == null || ClientSecretBox == null || InfraredIdBox == null || RemoteIdBox == null)
            {
                return;
            }

            _config.ClientSecret = ClientSecretBox.Visibility == Visibility.Visible 
                ? ClientSecretBox.Password 
                : ClientSecretVisibleBox.Text;

            if (DataCenterComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                _config.BaseUrl = selectedItem.Tag.ToString()!;
            }

            _config.InfraredId = InfraredIdBox.Text.Trim();
            _config.RemoteId = RemoteIdBox.Text.Trim();
            _config.IsSwapped = SwapCheckBox.IsChecked ?? false;

            _config.LastPower = _currentPower;
            _config.LastTemp = _currentTemp;
            _config.LastMode = _currentMode;
            _config.LastWind = _currentWind;

            if (!string.IsNullOrWhiteSpace(_lampuMeja.IpAddress))
                _config.LampuMejaIp = _lampuMeja.IpAddress;

            if (!string.IsNullOrWhiteSpace(_lampuLedStrip.IpAddress))
                _config.LampuLedStripIp = _lampuLedStrip.IpAddress;

            _config.Save();
        }

        private void OnConfigInputChanged(object sender, EventArgs e) => SaveCurrentConfig();

        private void OnSecretPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi) return;
            ClientSecretVisibleBox.Text = ClientSecretBox.Password;
            SaveCurrentConfig();
        }

        private void OnSecretTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi) return;
            ClientSecretBox.Password = ClientSecretVisibleBox.Text;
            SaveCurrentConfig();
        }

        private void ToggleSecretVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (ClientSecretBox.Visibility == Visibility.Visible)
            {
                ClientSecretVisibleBox.Text = ClientSecretBox.Password;
                ClientSecretBox.Visibility = Visibility.Collapsed;
                ClientSecretVisibleBox.Visibility = Visibility.Visible;
                ToggleSecretBtn.Content = "🔒";
            }
            else
            {
                ClientSecretBox.Password = ClientSecretVisibleBox.Text;
                ClientSecretVisibleBox.Visibility = Visibility.Collapsed;
                ClientSecretBox.Visibility = Visibility.Visible;
                ToggleSecretBtn.Content = "👁";
            }
        }

        private void SwapIds_Click(object sender, RoutedEventArgs e)
        {
            SwapCheckBox.IsChecked = !(SwapCheckBox.IsChecked ?? false);
        }

        private void SwapCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isSwapped = SwapCheckBox.IsChecked ?? false;
            UpdateSwapLabels(isSwapped);
            SaveCurrentConfig();
        }

        private void UpdateSwapLabels(bool isSwapped)
        {
            if (isSwapped)
            {
                InfraredLabel.Text = "Sub AC Remote (Swapped)";
                InfraredLabel.Foreground = (SolidColorBrush)FindResource("SuccessGreenBrush");
                RemoteLabel.Text = "Parent IR Blaster (Swapped)";
                RemoteLabel.Foreground = (SolidColorBrush)FindResource("AccentBlueBrush");
            }
            else
            {
                InfraredLabel.Text = "Parent IR Blaster ID";
                InfraredLabel.Foreground = (SolidColorBrush)FindResource("AccentBlueBrush");
                RemoteLabel.Text = "Sub AC Remote ID";
                RemoteLabel.Foreground = (SolidColorBrush)FindResource("SuccessGreenBrush");
            }
        }

        private async void TestToken_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentConfig();
            SetStatus("Mengambil Token...", "#38BDF8");
            var result = await _tuyaClient.EnsureAccessTokenAsync(forceRefresh: true);
            if (result.Success)
            {
                SetStatus("Token OK", "#10B981");
            }
            else
            {
                SetStatus("Token Gagal", "#EF4444");
            }
        }

        private async void TempMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemp > 16)
            {
                _currentTemp--;
                UpdateTempDisplay();
                SaveCurrentConfig();
                await ExecuteSendCommandAsync();
            }
        }

        private async void TempPlus_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemp < 30)
            {
                _currentTemp++;
                UpdateTempDisplay();
                SaveCurrentConfig();
                await ExecuteSendCommandAsync();
            }
        }

        private async void Wind_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int windVal))
            {
                _currentWind = windVal;
                UpdateSummaryText();
                SaveCurrentConfig();
                await ExecuteSendCommandAsync();
            }
        }

        private async void PowerOn_Click(object sender, RoutedEventArgs e)
        {
            _currentPower = 1;
            UpdateSummaryText();
            SaveCurrentConfig();
            await ExecuteSendCommandAsync();
        }

        private async void PowerOff_Click(object sender, RoutedEventArgs e)
        {
            _currentPower = 0;
            UpdateSummaryText();
            SaveCurrentConfig();
            await ExecuteSendCommandAsync();
        }

        private async Task ExecuteSendCommandAsync()
        {
            SaveCurrentConfig();
            SetStatus("Mengirim AC...", "#38BDF8");

            var result = await _tuyaClient.SendAcCommandAsync(_currentPower, _currentTemp, _currentMode, _currentWind);
            if (result.Success)
            {
                SetStatus("Terkirim Sukses", "#10B981");
            }
            else
            {
                SetStatus("Kirim Gagal", "#EF4444");
            }
        }

        // ==========================================
        // PHILIPS WIZ SMART LAMP KONTROL
        // ==========================================
        private async void ScanLamps_Click(object sender, RoutedEventArgs e)
        {
            await ScanLampsInBackgroundAsync();
        }

        private async Task InitialSyncLampsAsync()
        {
            LampScanStatusText.Text = "Membaca status lampu saat ini...";

            // 1. Instantly test known/cached IPs via direct unicast getPilot
            if (!string.IsNullOrWhiteSpace(_lampuMeja.IpAddress))
            {
                bool ok = await _wizClient.RefreshLampStatusAsync(_lampuMeja, 1500);
                if (ok) UpdateLampuMejaUi();
            }

            if (!string.IsNullOrWhiteSpace(_lampuLedStrip.IpAddress))
            {
                bool ok = await _wizClient.RefreshLampStatusAsync(_lampuLedStrip, 1500);
                if (ok) UpdateLampuLedStripUi();
            }

            // 2. Run discovery to refresh or confirm dynamic IP assignments
            await ScanLampsInBackgroundAsync();
        }

        private async Task ScanLampsInBackgroundAsync()
        {
            LampScanStatusText.Text = "Memindai lampu (Broadcast & Direct)...";

            var knownIps = new List<string>();
            if (!string.IsNullOrWhiteSpace(_lampuMeja.IpAddress)) knownIps.Add(_lampuMeja.IpAddress);
            if (!string.IsNullOrWhiteSpace(_lampuLedStrip.IpAddress)) knownIps.Add(_lampuLedStrip.IpAddress);
            knownIps.Add("192.168.1.2");
            knownIps.Add("192.168.1.4");

            var lamps = await _wizClient.DiscoverLampsAsync(knownIps.Distinct(), 2500);

            foreach (var lamp in lamps)
            {
                if (lamp.Mac.Equals("A8BB50B5A2E9", StringComparison.OrdinalIgnoreCase))
                {
                    if (lamp.IsOnline || string.IsNullOrWhiteSpace(_lampuMeja.IpAddress))
                    {
                        _lampuMeja = lamp;
                    }
                }
                else if (lamp.Mac.Equals("CC40858E7E82", StringComparison.OrdinalIgnoreCase))
                {
                    if (lamp.IsOnline || string.IsNullOrWhiteSpace(_lampuLedStrip.IpAddress))
                    {
                        _lampuLedStrip = lamp;
                    }
                }
            }

            // Fallback direct check if broadcast missed Lampu Meja
            if (!_lampuMeja.IsOnline && !string.IsNullOrWhiteSpace(_lampuMeja.IpAddress))
            {
                bool ok = await _wizClient.RefreshLampStatusAsync(_lampuMeja, 2000);
                if (ok) _lampuMeja.IsOnline = true;
            }

            // Fallback direct check if broadcast missed Lampu LED Strip
            if (!_lampuLedStrip.IsOnline && !string.IsNullOrWhiteSpace(_lampuLedStrip.IpAddress))
            {
                bool ok = await _wizClient.RefreshLampStatusAsync(_lampuLedStrip, 2000);
                if (ok) _lampuLedStrip.IsOnline = true;
            }

            UpdateLampuMejaUi();
            UpdateLampuLedStripUi();
            SaveCurrentConfig();

            int onlineCount = (_lampuMeja.IsOnline ? 1 : 0) + (_lampuLedStrip.IsOnline ? 1 : 0);
            LampScanStatusText.Text = $"{onlineCount} lampu online terdeteksi.";
        }

        private void UpdatePowerButtonStates(Button onBtn, Button offBtn, bool isOn)
        {
            if (isOn)
            {
                // ON button: Active Emerald Green
                onBtn.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                onBtn.Foreground = Brushes.White;
                onBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                onBtn.BorderThickness = new Thickness(1.5);
                onBtn.FontWeight = FontWeights.Bold;

                // OFF button: Inactive Subtle
                offBtn.Background = new SolidColorBrush(Color.FromRgb(34, 34, 52));
                offBtn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                offBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(54, 54, 84));
                offBtn.BorderThickness = new Thickness(1);
                offBtn.FontWeight = FontWeights.Normal;
            }
            else
            {
                // ON button: Inactive Subtle
                onBtn.Background = new SolidColorBrush(Color.FromRgb(34, 34, 52));
                onBtn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                onBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(54, 54, 84));
                onBtn.BorderThickness = new Thickness(1);
                onBtn.FontWeight = FontWeights.Normal;

                // OFF button: Active Ruby Red
                offBtn.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                offBtn.Foreground = Brushes.White;
                offBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                offBtn.BorderThickness = new Thickness(1.5);
                offBtn.FontWeight = FontWeights.Bold;
            }
        }

        private void UpdateLampuMejaUi()
        {
            Dispatcher.Invoke(() =>
            {
                _isSyncingLampUi = true;
                try
                {
                    LampuMejaIpText.Text = string.IsNullOrEmpty(_lampuMeja.IpAddress) ? "(Offline)" : $"({_lampuMeja.IpAddress})";
                    if (_lampuMeja.IsOnline)
                    {
                        LampuMejaStatus.Text = _lampuMeja.IsOn ? "🟢 Menyala" : "🔴 Mati";
                        LampuMejaStatus.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(_lampuMeja.IsOn ? "#10B981" : "#EF4444")!;
                        UpdatePowerButtonStates(LampuMejaOnBtn, LampuMejaOffBtn, _lampuMeja.IsOn);
                        LampuMejaBrightSlider.Value = _lampuMeja.Brightness;
                        LampuMejaBrightText.Text = $"{_lampuMeja.Brightness}%";
                        LampuMejaKelvinSlider.Value = _lampuMeja.Kelvin;
                        LampuMejaKelvinText.Text = $"{_lampuMeja.Kelvin}K";
                        UpdateKelvinPreview(LampuMejaKelvinPreview, _lampuMeja.Kelvin);
                    }
                    else
                    {
                        LampuMejaStatus.Text = "⚪ Offline";
                        LampuMejaStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                        UpdatePowerButtonStates(LampuMejaOnBtn, LampuMejaOffBtn, false);
                    }
                }
                finally
                {
                    _isSyncingLampUi = false;
                }
            });
        }

        private void UpdateLampuLedStripUi()
        {
            Dispatcher.Invoke(() =>
            {
                _isSyncingLampUi = true;
                try
                {
                    LampuLedStripIpText.Text = string.IsNullOrEmpty(_lampuLedStrip.IpAddress) ? "(Offline)" : $"({_lampuLedStrip.IpAddress})";
                    if (_lampuLedStrip.IsOnline)
                    {
                        LampuLedStripStatus.Text = _lampuLedStrip.IsOn ? "🟢 Menyala" : "🔴 Mati";
                        LampuLedStripStatus.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(_lampuLedStrip.IsOn ? "#10B981" : "#EF4444")!;
                        UpdatePowerButtonStates(LampuLedStripOnBtn, LampuLedStripOffBtn, _lampuLedStrip.IsOn);
                        LampuLedStripBrightSlider.Value = _lampuLedStrip.Brightness;
                        LampuLedStripBrightText.Text = $"{_lampuLedStrip.Brightness}%";
                        LampuLedStripKelvinSlider.Value = _lampuLedStrip.Kelvin;
                        LampuLedStripKelvinText.Text = $"{_lampuLedStrip.Kelvin}K";
                        UpdateKelvinPreview(LampuLedStripKelvinPreview, _lampuLedStrip.Kelvin);
                    }
                    else
                    {
                        LampuLedStripStatus.Text = "⚪ Offline";
                        LampuLedStripStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                        UpdatePowerButtonStates(LampuLedStripOnBtn, LampuLedStripOffBtn, false);
                    }
                }
                finally
                {
                    _isSyncingLampUi = false;
                }
            });
        }

        private void UpdateKelvinPreview(Border previewBorder, int kelvin)
        {
            string hex = WizLampClient.KelvinToHex(kelvin);
            previewBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        }

        // ==========================================
        // Lampu Meja Handlers
        // ==========================================
        private async void LampuMeja_On_Click(object sender, RoutedEventArgs e)
        {
            _lampuMeja.IsOnline = true;
            await _wizClient.SetPowerAsync(_lampuMeja, true);
            UpdateLampuMejaUi();
        }

        private async void LampuMeja_Off_Click(object sender, RoutedEventArgs e)
        {
            _lampuMeja.IsOnline = true;
            await _wizClient.SetPowerAsync(_lampuMeja, false);
            UpdateLampuMejaUi();
        }

        private void LampuMeja_BrightChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isSyncingLampUi) return;
            int val = (int)e.NewValue;
            LampuMejaBrightText.Text = $"{val}%";

            // Debounce rapid slider movement
            _mejaBrightCts?.Cancel();
            _mejaBrightCts = new CancellationTokenSource();
            var token = _mejaBrightCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(90, token);
                    if (!token.IsCancellationRequested)
                    {
                        await _wizClient.SetBrightnessAsync(_lampuMeja, val);
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        private void LampuMeja_KelvinChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isSyncingLampUi) return;
            int val = (int)e.NewValue;
            LampuMejaKelvinText.Text = $"{val}K";
            UpdateKelvinPreview(LampuMejaKelvinPreview, val);

            // Debounce rapid slider movement
            _mejaKelvinCts?.Cancel();
            _mejaKelvinCts = new CancellationTokenSource();
            var token = _mejaKelvinCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(90, token);
                    if (!token.IsCancellationRequested)
                    {
                        await _wizClient.SetKelvinAsync(_lampuMeja, val);
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        private async void LampuMeja_Warm_Click(object sender, RoutedEventArgs e)
        {
            _isSyncingLampUi = true;
            LampuMejaKelvinSlider.Value = 2700;
            LampuMejaKelvinText.Text = "2700K";
            UpdateKelvinPreview(LampuMejaKelvinPreview, 2700);
            _isSyncingLampUi = false;
            await _wizClient.SetKelvinAsync(_lampuMeja, 2700);
        }

        private async void LampuMeja_Daylight_Click(object sender, RoutedEventArgs e)
        {
            _isSyncingLampUi = true;
            LampuMejaKelvinSlider.Value = 6500;
            LampuMejaKelvinText.Text = "6500K";
            UpdateKelvinPreview(LampuMejaKelvinPreview, 6500);
            _isSyncingLampUi = false;
            await _wizClient.SetKelvinAsync(_lampuMeja, 6500);
        }

        private async void LampuMeja_Red_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuMeja, 255, 0, 0);
        private async void LampuMeja_Green_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuMeja, 0, 255, 0);
        private async void LampuMeja_Blue_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuMeja, 0, 0, 255);

        // ==========================================
        // Lampu LED Strip Handlers
        // ==========================================
        private async void LampuLedStrip_On_Click(object sender, RoutedEventArgs e)
        {
            _lampuLedStrip.IsOnline = true;
            await _wizClient.SetPowerAsync(_lampuLedStrip, true);
            UpdateLampuLedStripUi();
        }

        private async void LampuLedStrip_Off_Click(object sender, RoutedEventArgs e)
        {
            _lampuLedStrip.IsOnline = true;
            await _wizClient.SetPowerAsync(_lampuLedStrip, false);
            UpdateLampuLedStripUi();
        }

        private void LampuLedStrip_BrightChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isSyncingLampUi) return;
            int val = (int)e.NewValue;
            LampuLedStripBrightText.Text = $"{val}%";

            // Debounce rapid slider movement
            _stripBrightCts?.Cancel();
            _stripBrightCts = new CancellationTokenSource();
            var token = _stripBrightCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(90, token);
                    if (!token.IsCancellationRequested)
                    {
                        await _wizClient.SetBrightnessAsync(_lampuLedStrip, val);
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        private void LampuLedStrip_KelvinChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isSyncingLampUi) return;
            int val = (int)e.NewValue;
            LampuLedStripKelvinText.Text = $"{val}K";
            UpdateKelvinPreview(LampuLedStripKelvinPreview, val);

            // Debounce rapid slider movement
            _stripKelvinCts?.Cancel();
            _stripKelvinCts = new CancellationTokenSource();
            var token = _stripKelvinCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(90, token);
                    if (!token.IsCancellationRequested)
                    {
                        await _wizClient.SetKelvinAsync(_lampuLedStrip, val);
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        private async void LampuLedStrip_Warm_Click(object sender, RoutedEventArgs e)
        {
            _isSyncingLampUi = true;
            LampuLedStripKelvinSlider.Value = 2700;
            LampuLedStripKelvinText.Text = "2700K";
            UpdateKelvinPreview(LampuLedStripKelvinPreview, 2700);
            _isSyncingLampUi = false;
            await _wizClient.SetKelvinAsync(_lampuLedStrip, 2700);
        }

        private async void LampuLedStrip_Daylight_Click(object sender, RoutedEventArgs e)
        {
            _isSyncingLampUi = true;
            LampuLedStripKelvinSlider.Value = 6500;
            LampuLedStripKelvinText.Text = "6500K";
            UpdateKelvinPreview(LampuLedStripKelvinPreview, 6500);
            _isSyncingLampUi = false;
            await _wizClient.SetKelvinAsync(_lampuLedStrip, 6500);
        }

        private async void LampuLedStrip_Red_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuLedStrip, 255, 0, 0);
        private async void LampuLedStrip_Purple_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuLedStrip, 180, 0, 255);
        private async void LampuLedStrip_Blue_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuLedStrip, 0, 100, 255);
        private async void LampuLedStrip_Cyan_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuLedStrip, 0, 255, 230);

        // ==========================================
        // LOG CONSOLE
        // ==========================================
        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(LogTextBox.Text))
                {
                    Clipboard.SetText(LogTextBox.Text);
                    AppendLog("Log disalin ke clipboard.", false);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Gagal menyalin log: {ex.Message}", true);
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            AppendLog("Log dibersihkan.", false);
        }

        private void ToggleLog_Click(object sender, RoutedEventArgs e)
        {
            if (LogContainer.Visibility == Visibility.Visible)
            {
                LogContainer.Visibility = Visibility.Collapsed;
                ToggleLogBtn.Foreground = (SolidColorBrush)FindResource("TextMutedBrush");
            }
            else
            {
                LogContainer.Visibility = Visibility.Visible;
                ToggleLogBtn.Foreground = (SolidColorBrush)FindResource("AccentBlueBrush");
                LogTextBox.ScrollToEnd();
            }
        }
    }
}