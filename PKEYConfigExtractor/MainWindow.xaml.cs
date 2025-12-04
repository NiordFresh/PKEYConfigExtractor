using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using Microsoft.Win32;

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

            MenuItem copySelected = new MenuItem() { Header = "Copy Selected" };
            copySelected.Click += CopySelected_Click;

            MenuItem copyAll = new MenuItem() { Header = "Copy All" };
            copyAll.Click += CopyAll_Click;

            ContextMenu contextMenu = new ContextMenu();
            contextMenu.Items.Add(copySelected);
            contextMenu.Items.Add(copyAll);

            KeysListBox.ContextMenu = contextMenu;
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

                    string tempPath = Path.GetTempFileName();
                    string finalPath = Path.ChangeExtension(tempPath, ".pyc");
                    File.Move(tempPath, finalPath);

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

                EditionComboBox.Items.Clear();

                var configs = pkeyConfig.Configs
                    .Where(c => {
                        var pubKey = pkeyConfig.PubKeyForGroup(c.GroupId);
                        return pubKey != null && pubKey.Algorithm == "msft:rm/algorithm/pkey/2009";
                    })
                    .ToList();

                foreach (var config in configs)
                {
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = $"[{config.GroupId}]: \"{config.Desc}\" - {config.EditionId}",
                        Tag = config
                    };
                    EditionComboBox.Items.Add(item);
                }

                if (EditionComboBox.Items.Count > 0)
                {
                    EditionComboBox.SelectedIndex = 0;
                }

                MessageBox.Show($"Loaded {configs.Count} editions successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    FileName = "python",
                    Arguments = $"\"{keycutterPath}\" encode {groupId} {serial} {security}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return "[Error: Could not start Python process]";
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    process.WaitForExit(5000);

                    if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                    {
                        return $"[Python Error: {error.Trim()}]";
                    }

                    string key = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault()?.Trim();

                    if (string.IsNullOrEmpty(key))
                    {
                        return "[Error: No output from Python]";
                    }

                    return key;
                }
            }
            catch (Exception ex)
            {
                return $"[Error: {ex.Message}]";
            }
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (EditionComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an edition first!", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (pkeyConfig == null)
            {
                MessageBox.Show("Please load a PKEYConfig file first!", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(keycutterPath))
            {
                keycutterPath = ExtractEmbeddedResource("keycutter.cpython-314.pyc");
                if (string.IsNullOrEmpty(keycutterPath))
                {
                    return;
                }
            }

            try
            {
                var selectedItem = (ComboBoxItem)EditionComboBox.SelectedItem;
                var config = (PKeyConfig.Configuration)selectedItem.Tag;

                bool useRandom = RandomCheckBox.IsChecked == true;

                int baseSerial;
                long baseSecurity;

                if (useRandom)
                {
                    Random rand = new Random();
                    baseSerial = rand.Next(0, 0x3FFFFFFF);
                    byte[] securityBytes = new byte[8];
                    rand.NextBytes(securityBytes);
                    baseSecurity = BitConverter.ToInt64(securityBytes, 0) & 0x1FFFFFFFFFFFFF;
                }
                else
                {
                    if (!int.TryParse(SerialTextBox.Text.Replace("0x", "").Replace("[", "").Replace("]", ""),
                        System.Globalization.NumberStyles.HexNumber, null, out baseSerial))
                    {
                        if (!int.TryParse(SerialTextBox.Text, out baseSerial))
                        {
                            baseSerial = 0;
                        }
                    }

                    string securityText = SecurityTextBox.Text.Replace("0x", "").Replace("[", "").Replace("]", "");
                    if (!long.TryParse(securityText, System.Globalization.NumberStyles.HexNumber, null, out baseSecurity))
                    {
                        if (!long.TryParse(SecurityTextBox.Text, out baseSecurity))
                        {
                            baseSecurity = 0;
                        }
                    }
                }

                int count = int.Parse(CountTextBox.Text);
                if (count < 1) count = 1;
                if (count > 100) count = 100;

                KeysListBox.Items.Clear();

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
                    KeysListBox.Items.Add(displayText);

                    Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                }

                MessageBox.Show($"Generated {count} key(s) successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating keys: {ex.Message}\n\n{ex.StackTrace}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
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
            foreach (var config in configurations)
            {
                Configs.Add(new Configuration
                {
                    ConfigId = config.Elements().FirstOrDefault(e => e.Name.LocalName == "ActConfigId")?.Value,
                    GroupId = int.Parse(config.Elements().FirstOrDefault(e => e.Name.LocalName == "RefGroupId")?.Value ?? "0"),
                    EditionId = config.Elements().FirstOrDefault(e => e.Name.LocalName == "EditionId")?.Value,
                    Desc = config.Elements().FirstOrDefault(e => e.Name.LocalName == "ProductDescription")?.Value,
                    KeyType = config.Elements().FirstOrDefault(e => e.Name.LocalName == "ProductKeyType")?.Value,
                    Randomized = config.Elements().FirstOrDefault(e => e.Name.LocalName == "IsRandomized")?.Value == "true"
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