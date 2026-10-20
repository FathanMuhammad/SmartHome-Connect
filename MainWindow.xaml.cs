using System;
using System.Collections.Generic;
using System.Linq;
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
        private WizLampDevice _lampuMeja = new() { Mac = "A8BB50B5A2E9", Name = "Lampu Meja" };
        private WizLampDevice _lampuLedStrip = new() { Mac = "CC40858E7E82", Name = "Lampu LED Strip" };

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

            // Trigger non-blocking lamp discovery on start
            _ = ScanLampsInBackgroundAsync();
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

            // Restore cached Lamp IPs if available
            if (!string.IsNullOrWhiteSpace(_config.LampuMejaIp))
            {
                _lampuMeja.IpAddress = _config.LampuMejaIp;
                LampuMejaIpText.Text = $"({_config.LampuMejaIp})";
            }
            if (!string.IsNullOrWhiteSpace(_config.LampuLedStripIp))
            {
                _lampuLedStrip.IpAddress = _config.LampuLedStripIp;
                LampuLedStripIpText.Text = $"({_config.LampuLedStripIp})";
            }

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

            _config.LampuMejaIp = _lampuMeja.IpAddress;
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

        private async Task ScanLampsInBackgroundAsync()
        {
            LampScanStatusText.Text = "Memindai lampu via UDP Broadcast...";
            var lamps = await _wizClient.DiscoverLampsAsync(2000);

            foreach (var lamp in lamps)
            {
                if (lamp.Mac.Equals("A8BB50B5A2E9", StringComparison.OrdinalIgnoreCase))
                {
                    _lampuMeja = lamp;
                    UpdateLampuMejaUi();
                }
                else if (lamp.Mac.Equals("CC40858E7E82", StringComparison.OrdinalIgnoreCase))
                {
                    _lampuLedStrip = lamp;
                    UpdateLampuLedStripUi();
                }
            }

            SaveCurrentConfig();
            int onlineCount = lamps.Count(l => l.IsOnline);
            LampScanStatusText.Text = $"{onlineCount} lampu online ditemukan.";
        }

        private void UpdateLampuMejaUi()
        {
            Dispatcher.Invoke(() =>
            {
                LampuMejaIpText.Text = string.IsNullOrEmpty(_lampuMeja.IpAddress) ? "(Offline)" : $"({_lampuMeja.IpAddress})";
                if (_lampuMeja.IsOnline)
                {
                    LampuMejaStatus.Text = _lampuMeja.IsOn ? "🟢 Menyala" : "🔴 Mati";
                    LampuMejaStatus.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(_lampuMeja.IsOn ? "#10B981" : "#EF4444")!;
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
                }
            });
        }

        private void UpdateLampuLedStripUi()
        {
            Dispatcher.Invoke(() =>
            {
                LampuLedStripIpText.Text = string.IsNullOrEmpty(_lampuLedStrip.IpAddress) ? "(Offline)" : $"({_lampuLedStrip.IpAddress})";
                if (_lampuLedStrip.IsOnline)
                {
                    LampuLedStripStatus.Text = _lampuLedStrip.IsOn ? "🟢 Menyala" : "🔴 Mati";
                    LampuLedStripStatus.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(_lampuLedStrip.IsOn ? "#10B981" : "#EF4444")!;
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
                }
            });
        }

        private void UpdateKelvinPreview(Border previewBorder, int kelvin)
        {
            string hex = WizLampClient.KelvinToHex(kelvin);
            previewBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        }

        // Lampu Meja Handlers
        private async void LampuMeja_On_Click(object sender, RoutedEventArgs e)
        {
            await _wizClient.SetPowerAsync(_lampuMeja, true);
            UpdateLampuMejaUi();
        }

        private async void LampuMeja_Off_Click(object sender, RoutedEventArgs e)
        {
            await _wizClient.SetPowerAsync(_lampuMeja, false);
            UpdateLampuMejaUi();
        }

        private async void LampuMeja_BrightChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            int val = (int)e.NewValue;
            LampuMejaBrightText.Text = $"{val}%";
            await _wizClient.SetBrightnessAsync(_lampuMeja, val);
        }

        private async void LampuMeja_KelvinChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            int val = (int)e.NewValue;
            LampuMejaKelvinText.Text = $"{val}K";
            UpdateKelvinPreview(LampuMejaKelvinPreview, val);
            await _wizClient.SetKelvinAsync(_lampuMeja, val);
        }

        private async void LampuMeja_Warm_Click(object sender, RoutedEventArgs e)
        {
            LampuMejaKelvinSlider.Value = 2700;
            await _wizClient.SetKelvinAsync(_lampuMeja, 2700);
        }

        private async void LampuMeja_Daylight_Click(object sender, RoutedEventArgs e)
        {
            LampuMejaKelvinSlider.Value = 6500;
            await _wizClient.SetKelvinAsync(_lampuMeja, 6500);
        }

        private async void LampuMeja_Red_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuMeja, 255, 0, 0);
        private async void LampuMeja_Green_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuMeja, 0, 255, 0);
        private async void LampuMeja_Blue_Click(object sender, RoutedEventArgs e) => await _wizClient.SetRgbAsync(_lampuMeja, 0, 0, 255);

        // Lampu LED Strip Handlers
        private async void LampuLedStrip_On_Click(object sender, RoutedEventArgs e)
        {
            await _wizClient.SetPowerAsync(_lampuLedStrip, true);
            UpdateLampuLedStripUi();
        }

        private async void LampuLedStrip_Off_Click(object sender, RoutedEventArgs e)
        {
            await _wizClient.SetPowerAsync(_lampuLedStrip, false);
            UpdateLampuLedStripUi();
        }

        private async void LampuLedStrip_BrightChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            int val = (int)e.NewValue;
            LampuLedStripBrightText.Text = $"{val}%";
            await _wizClient.SetBrightnessAsync(_lampuLedStrip, val);
        }

        private async void LampuLedStrip_KelvinChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            int val = (int)e.NewValue;
            LampuLedStripKelvinText.Text = $"{val}K";
            UpdateKelvinPreview(LampuLedStripKelvinPreview, val);
            await _wizClient.SetKelvinAsync(_lampuLedStrip, val);
        }

        private async void LampuLedStrip_Warm_Click(object sender, RoutedEventArgs e)
        {
            LampuLedStripKelvinSlider.Value = 2700;
            await _wizClient.SetKelvinAsync(_lampuLedStrip, 2700);
        }

        private async void LampuLedStrip_Daylight_Click(object sender, RoutedEventArgs e)
        {
            LampuLedStripKelvinSlider.Value = 6500;
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
    }
}