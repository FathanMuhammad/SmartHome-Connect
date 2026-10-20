using System;
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

        private int _currentPower = 1;
        private int _currentTemp = 22;
        private int _currentMode = 0; // 0=Cool, 1=Heat, 2=Auto, 3=Fan, 4=Dry
        private int _currentWind = 0; // 0=Auto, 1=Low, 2=Med, 3=High
        private bool _isUpdatingUi = false;
        private bool _isInitialized = false;

        public MainWindow()
        {
            // Initialize config and client before XAML components load
            _config = AppConfig.Load();
            _tuyaClient = new TuyaClient(_config);
            _tuyaClient.OnLog += AppendLog;

            InitializeComponent();

            InitializeUiState();
            _isInitialized = true;
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

            // Hook up selection change after initial value set
            DataCenterComboBox.SelectionChanged += OnConfigInputChanged;

            // Restore AC states
            _currentPower = _config.LastPower;
            _currentTemp = Math.Clamp(_config.LastTemp, 16, 30);
            _currentMode = Math.Clamp(_config.LastMode, 0, 4);
            _currentWind = Math.Clamp(_config.LastWind, 0, 3);

            UpdateTempDisplay();

            // Set Mode RadioButton
            foreach (RadioButton rb in ModeGrid.Children.OfType<RadioButton>())
            {
                if (int.TryParse(rb.Tag?.ToString(), out int tagVal) && tagVal == _currentMode)
                {
                    rb.IsChecked = true;
                    break;
                }
            }

            // Set Wind RadioButton
            foreach (RadioButton rb in WindGrid.Children.OfType<RadioButton>())
            {
                if (int.TryParse(rb.Tag?.ToString(), out int tagVal) && tagVal == _currentWind)
                {
                    rb.IsChecked = true;
                    break;
                }
            }

            _isUpdatingUi = false;
            UpdateSummaryText();

            AppendLog($"Aplikasi siap. Konfigurasi dimuat dari config.json.", false);
            if (string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                AppendLog("Perhatian: Client Secret belum diisi. Masukkan Client Secret di panel Pengaturan.", true);
                SetStatus("Kredensial Kosong", "#F59E0B");
                SettingsExpander.IsExpanded = true;
            }
            else
            {
                AppendLog("Kredensial Client Secret terdeteksi.", false);
                SetStatus("Siap", "#10B981");
            }
        }

        private void AppendLog(string message, bool isError)
        {
            Dispatcher.InvokeAsync(() =>
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.ScrollToEnd();

                if (isError)
                {
                    LogTextBox.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Soft red
                }
                else
                {
                    LogTextBox.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Soft green
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

        private void UpdateSummaryText()
        {
            string modeName = _currentMode switch
            {
                0 => "Cool",
                1 => "Heat",
                2 => "Auto",
                3 => "Fan",
                4 => "Dry",
                _ => "Unknown"
            };

            string windName = _currentWind switch
            {
                0 => "Fan Auto",
                1 => "Fan Low",
                2 => "Fan Med",
                3 => "Fan High",
                _ => "Auto"
            };

            string powerStatus = _currentPower == 1 ? "ON" : "OFF";
            CurrentStateSummary.Text = $"{powerStatus} • {modeName} • {windName}";
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

            _config.Save();
        }

        private void OnConfigInputChanged(object sender, EventArgs e)
        {
            SaveCurrentConfig();
        }

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

        private void TempMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemp > 16)
            {
                _currentTemp--;
                UpdateTempDisplay();
                SaveCurrentConfig();
            }
        }

        private void TempPlus_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTemp < 30)
            {
                _currentTemp++;
                UpdateTempDisplay();
                SaveCurrentConfig();
            }
        }

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int modeVal))
            {
                _currentMode = modeVal;
                UpdateSummaryText();
                SaveCurrentConfig();
            }
        }

        private void Wind_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int windVal))
            {
                _currentWind = windVal;
                UpdateSummaryText();
                SaveCurrentConfig();
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

        private async void SendCommand_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteSendCommandAsync();
        }

        private async void PresetQuickCool_Click(object sender, RoutedEventArgs e)
        {
            // 18°C Cool High
            _currentPower = 1;
            _currentTemp = 18;
            _currentMode = 0; // Cool
            _currentWind = 3; // High
            ApplyStateToUi();
            await ExecuteSendCommandAsync();
        }

        private async void PresetWorkComfort_Click(object sender, RoutedEventArgs e)
        {
            // 22°C Cool Auto
            _currentPower = 1;
            _currentTemp = 22;
            _currentMode = 0; // Cool
            _currentWind = 0; // Auto
            ApplyStateToUi();
            await ExecuteSendCommandAsync();
        }

        private async void PresetSleepMode_Click(object sender, RoutedEventArgs e)
        {
            // 25°C Cool Low
            _currentPower = 1;
            _currentTemp = 25;
            _currentMode = 0; // Cool
            _currentWind = 1; // Low
            ApplyStateToUi();
            await ExecuteSendCommandAsync();
        }

        private void ApplyStateToUi()
        {
            _isUpdatingUi = true;
            UpdateTempDisplay();

            foreach (RadioButton rb in ModeGrid.Children.OfType<RadioButton>())
            {
                if (int.TryParse(rb.Tag?.ToString(), out int tagVal))
                {
                    rb.IsChecked = (tagVal == _currentMode);
                }
            }

            foreach (RadioButton rb in WindGrid.Children.OfType<RadioButton>())
            {
                if (int.TryParse(rb.Tag?.ToString(), out int tagVal))
                {
                    rb.IsChecked = (tagVal == _currentWind);
                }
            }

            _isUpdatingUi = false;
            UpdateSummaryText();
            SaveCurrentConfig();
        }

        private async Task ExecuteSendCommandAsync()
        {
            SaveCurrentConfig();
            SetStatus("Mengirim Perintah...", "#38BDF8");

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