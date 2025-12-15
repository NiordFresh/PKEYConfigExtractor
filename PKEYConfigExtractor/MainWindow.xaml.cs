using Microsoft.Win32;
using PKEYConfigExtractor;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;

namespace PKEYConfigExtractor
{
    public partial class MainWindow : Window
    {
        private PKeyConfig pkeyConfig;
        private string keycutterPath = null;

        public MainWindow()
        {
            InitializeComponent();
            FilePathTextBox.PreviewMouseLeftButtonDown += FilePathTextBox_Click;
            GenerateButton.Click += GenerateButton_Click;
            ManualRadioButton.Checked += ManualRadioButton_Checked;
            ComboRadioButton.Checked += ComboRadioButton_Checked;

            MenuItem copySelected = new MenuItem() { Header = "Copy Selected" };
            copySelected.Click += CopySelected_Click;

            MenuItem copyAll = new MenuItem() { Header = "Copy All" };
            copyAll.Click += CopyAll_Click;

            ContextMenu contextMenu = new ContextMenu();
            contextMenu.Items.Add(copySelected);
            contextMenu.Items.Add(copyAll);

            KeysListBox.ContextMenu = contextMenu;

            MainComboBox.IsEnabled = true;
            GroupIdTextBox.IsEnabled = false;
            ComboRadioButton.IsChecked = true;
        }

        private string ExtractEmbeddedResource(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string fullResourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith(resourceName));

                if (string.IsNullOrEmpty(fullResourceName))
                {
                    throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
                }

                using (Stream stream = assembly.GetManifestResourceStream(fullResourceName))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException($"Could not load resource stream for '{resourceName}'.");
                    }
                    string appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "PKEYConfigExtractor"
                    );

                    if (!Directory.Exists(appDataPath))
                    {
                        Directory.CreateDirectory(appDataPath);
                    }

                    string finalPath = Path.Combine(appDataPath, resourceName);

                    using (FileStream fileStream = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }

                    return finalPath;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error extracting embedded resource: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void CleanupTempFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    System.Threading.Thread.Sleep(100);
                    File.Delete(path);
                }
                catch
                {
                }
            }
        }

        private void FilePathTextBox_Click(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "XRM-MS files (*.xrm-ms)|*.xrm-ms|All files (*.*)|*.*",
                Title = "Select PKEYConfig file"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FilePathTextBox.Text = openFileDialog.FileName;
                LoadPKeyConfig(openFileDialog.FileName);
            }
        }

        private void LoadPKeyConfig(string filePath)
        {
            try
            {
                string xmlContent = File.ReadAllText(filePath);
                XDocument xdoc = XDocument.Parse(xmlContent);

                pkeyConfig = new PKeyConfig(xdoc.Root);

                var items = new ObservableCollection<EditionItem>();

                var configs = pkeyConfig.Configs
                    .Where(c => {
                        var pubKey = pkeyConfig.PubKeyForGroup(c.GroupId);
                        return pubKey != null && pubKey.Algorithm == "msft:rm/algorithm/pkey/2009";
                    })
                    .ToList();

                foreach (var config in configs)
                {
                    var item = new EditionItem(
                        $"[{config.GroupId}]: \"{config.Desc}\" - {config.EditionId}",
                        config
                    );
                    items.Add(item);
                }

                EditionListBox.ItemsSource = items;

                MessageBox.Show($"Loaded {configs.Count} editions successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditionListBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is CheckBox)
            {
                e.Handled = false;
            }
        }

        private void ManualRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            MainComboBox.IsEnabled = false;
            GroupIdTextBox.IsEnabled = true;
            GroupIdTextBox.Focus();
        }

        private void ComboRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            MainComboBox.IsEnabled = true;
            GroupIdTextBox.IsEnabled = false;
        }

        private string GenerateKeyWithPython(int groupId, int serial, long security)
        {
            if (string.IsNullOrEmpty(keycutterPath) || !File.Exists(keycutterPath))
            {
                MessageBox.Show("Keycutter resource not found or not extracted.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return "[Error: Keycutter not available]";
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = keycutterPath,
                    Arguments = $"encode {groupId} {serial} {security}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(keycutterPath)
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return "[Error: Could not start keycutter process]";
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    bool exited = process.WaitForExit(10000);

                    if (!exited)
                    {
                        process.Kill();
                        return "[Error: Keycutter process timeout]";
                    }

                    if (process.ExitCode != 0)
                    {
                        return $"[Error: Exit code {process.ExitCode}]";
                    }

                    if (!string.IsNullOrEmpty(error))
                    {
                        return $"[Python Error: {error.Trim()}]";
                    }

                    string key = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault()?.Trim();

                    if (string.IsNullOrEmpty(key))
                    {
                        return "[Error: No output from keycutter]";
                    }

                    return key;
                }
            }
            catch (Exception ex)
            {
                return $"[Exception: {ex.Message}]";
            }
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (keycutterPath == null)
                {
                    keycutterPath = ExtractEmbeddedResource("keycutter.exe");

                    if (keycutterPath == null)
                    {
                        MessageBox.Show("Failed to extract keycutter.exe", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                var selectedConfigs = new List<PKeyConfig.Configuration>();
                if (ManualRadioButton.IsChecked == true)
                {
                    if (!int.TryParse(GroupIdTextBox.Text, out int groupId))
                    {
                        MessageBox.Show("Please enter a valid Group ID!", "Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var manualConfig = new PKeyConfig.Configuration
                    {
                        GroupId = groupId,
                        ConfigId = $"manual_{groupId}",
                        EditionId = "Manual",
                        Desc = "Manual Entry",
                        KeyType = "Manual",
                        Randomized = false
                    };

                    selectedConfigs.Add(manualConfig);
                }
                else 
                {
                    if (EditionListBox.ItemsSource is IEnumerable<EditionItem> items)
                    {
                        foreach (var item in items)
                        {
                            if (item.IsChecked && item.Config is PKeyConfig.Configuration config)
                            {
                                selectedConfigs.Add(config);
                            }
                        }
                    }
                }

                if (selectedConfigs.Count == 0)
                {
                    MessageBox.Show("Please select at least one edition!", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool useRandom = RandomCheckBox.IsChecked == true;
                int baseSerial = 0;
                long baseSecurity = 0;

                if (!useRandom)
                {
                    if (!int.TryParse(SerialTextBox.Text, out baseSerial))
                    {
                        baseSerial = 0;
                    }
                    if (!long.TryParse(SecurityTextBox.Text, out baseSecurity))
                    {
                        baseSecurity = 0;
                    }
                }

                int count = int.Parse(CountTextBox.Text);
                if (count < 1) count = 1;
                if (count > 500) count = 500;

                int totalKeys = selectedConfigs.Count * count;


                GenerationProgressBar.Visibility = Visibility.Visible;
                GenerationProgressBar.Maximum = totalKeys;
                GenerationProgressBar.Value = 0;

                GenerateButton.IsEnabled = false;

                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() => KeysListBox.Items.Clear());

                    int generatedCount = 0;

                    foreach (var config in selectedConfigs)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int currentSerial;
                            long currentSecurity;

                            if (useRandom)
                            {
                                Random rand = new Random(Guid.NewGuid().GetHashCode());
                                currentSerial = rand.Next(0, 0x3FFFFFFF);
                                byte[] securityBytes = new byte[8];
                                rand.NextBytes(securityBytes);
                                currentSecurity = BitConverter.ToInt64(securityBytes, 0) & 0x1FFFFFFFFFFFFF;
                            }
                            else
                            {
                                currentSerial = baseSerial + i;
                                currentSecurity = baseSecurity;
                            }
                            string key = GenerateKeyWithPython(config.GroupId, currentSerial, currentSecurity);
                            string displayText = $"{key} - [{config.Desc}] ({config.GroupId}, {currentSerial:X}, {currentSecurity:X})";
                            Dispatcher.Invoke(() => KeysListBox.Items.Add(displayText));
                            generatedCount++;
                            Dispatcher.Invoke(() => GenerationProgressBar.Value = generatedCount);
                        }
                    }
                });

                GenerateButton.IsEnabled = true;
                GenerationProgressBar.Visibility = Visibility.Collapsed;

                MessageBox.Show($"Generated {totalKeys} key(s) successfully for {selectedConfigs.Count} edition(s)!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating keys: {ex.Message}\n\n{ex.StackTrace}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                GenerationProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (KeysListBox.SelectedItem != null)
            {
                string selectedText = KeysListBox.SelectedItem.ToString();
                string key = selectedText.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim();
                Clipboard.SetText(key);
                MessageBox.Show("Key copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (KeysListBox.Items.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var item in KeysListBox.Items)
                {
                    string text = item.ToString();
                    string key = text.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim();
                    sb.AppendLine(key);
                }
                string allKeys = sb.ToString().TrimEnd();
                Clipboard.SetText(allKeys);
                MessageBox.Show($"Copied {KeysListBox.Items.Count} key(s) to clipboard!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupTempFile(keycutterPath);
            base.OnClosed(e);
        }
    }

    public class PKeyConfig
    {
        public List<Configuration> Configs { get; private set; }
        public List<KeyRange> Ranges { get; private set; }
        public List<PublicKey> PubKeys { get; private set; }

        public class Configuration
        {
            public string ConfigId { get; set; }
            public int GroupId { get; set; }
            public string EditionId { get; set; }
            public string Desc { get; set; }
            public string KeyType { get; set; }
            public bool Randomized { get; set; }
        }

        public class KeyRange
        {
            public string ConfigId { get; set; }
            public string PartNumber { get; set; }
            public string EulaType { get; set; }
            public bool IsValid { get; set; }
            public int Start { get; set; }
            public int End { get; set; }
        }

        public class PublicKey
        {
            public int GroupId { get; set; }
            public string Algorithm { get; set; }
            public string PubKeyValue { get; set; }
        }

        public PKeyConfig(XElement xml)
        {
            Configs = new List<Configuration>();
            Ranges = new List<KeyRange>();
            PubKeys = new List<PublicKey>();

            var infoBinElement = xml.Descendants()
                .Where(e => e.Name.LocalName == "infoBin")
                .FirstOrDefault(e => e.Attribute("name")?.Value == "pkeyConfigData");

            if (infoBinElement == null)
                throw new Exception("pkeyConfigData not found in XML");

            string base64Data = infoBinElement.Value;
            byte[] data = Convert.FromBase64String(base64Data);
            string decodedXml = Encoding.UTF8.GetString(data);

            XDocument pkeyConfigDoc = XDocument.Parse(decodedXml);
            XElement root = pkeyConfigDoc.Root;

            var configurations = root.Descendants().Where(e => e.Name.LocalName == "Configuration");
            foreach (var configElement in configurations)
            {
                Configs.Add(new Configuration
                {
                    ConfigId = configElement.Elements().FirstOrDefault(e => e.Name.LocalName == "ActConfigId")?.Value,
                    GroupId = int.Parse(configElement.Elements().FirstOrDefault(e => e.Name.LocalName == "RefGroupId")?.Value ?? "0"),
                    EditionId = configElement.Elements().FirstOrDefault(e => e.Name.LocalName == "EditionId")?.Value,
                    Desc = configElement.Elements().FirstOrDefault(e => e.Name.LocalName == "ProductDescription")?.Value,
                    KeyType = configElement.Elements().FirstOrDefault(e => e.Name.LocalName == "ProductKeyType")?.Value,
                    Randomized = configElement.Elements().FirstOrDefault(e => e.Name.LocalName == "IsRandomized")?.Value == "true"
                });
            }

            var keyRanges = root.Descendants().Where(e => e.Name.LocalName == "KeyRange");
            foreach (var range in keyRanges)
            {
                Ranges.Add(new KeyRange
                {
                    ConfigId = range.Elements().FirstOrDefault(e => e.Name.LocalName == "RefActConfigId")?.Value,
                    PartNumber = range.Elements().FirstOrDefault(e => e.Name.LocalName == "PartNumber")?.Value,
                    EulaType = range.Elements().FirstOrDefault(e => e.Name.LocalName == "EulaType")?.Value,
                    IsValid = range.Elements().FirstOrDefault(e => e.Name.LocalName == "IsValid")?.Value == "true",
                    Start = int.Parse(range.Elements().FirstOrDefault(e => e.Name.LocalName == "Start")?.Value ?? "0"),
                    End = int.Parse(range.Elements().FirstOrDefault(e => e.Name.LocalName == "End")?.Value ?? "0")
                });
            }

            var publicKeys = root.Descendants().Where(e => e.Name.LocalName == "PublicKey");
            foreach (var pubkey in publicKeys)
            {
                PubKeys.Add(new PublicKey
                {
                    GroupId = int.Parse(pubkey.Elements().FirstOrDefault(e => e.Name.LocalName == "GroupId")?.Value ?? "0"),
                    Algorithm = pubkey.Elements().FirstOrDefault(e => e.Name.LocalName == "AlgorithmId")?.Value,
                    PubKeyValue = pubkey.Elements().FirstOrDefault(e => e.Name.LocalName == "PublicKeyValue")?.Value
                });
            }
        }

        public Configuration ConfigForGroup(int group)
        {
            return Configs.FirstOrDefault(x => x.GroupId == group);
        }

        public List<KeyRange> RangesForGroup(int group)
        {
            var conf = ConfigForGroup(group);
            return Ranges.Where(x => x.ConfigId == conf?.ConfigId).ToList();
        }

        public PublicKey PubKeyForGroup(int group)
        {
            return PubKeys.FirstOrDefault(x => x.GroupId == group);
        }
    }
}

public class EditionItem : INotifyPropertyChanged
{
    private bool _isChecked;
    public string Display { get; set; }
    public PKeyConfig.Configuration Config { get; set; }

    public bool IsChecked
    {
        get { return _isChecked; }
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        }
    }

    public EditionItem(string display, PKeyConfig.Configuration config)
    {
        Display = display;
        Config = config;
        _isChecked = false;
    }

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}