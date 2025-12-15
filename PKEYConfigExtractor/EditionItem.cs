using System.ComponentModel;

namespace PKEYConfigExtractor
{
    public class EditionItem : INotifyPropertyChanged
    {
        private bool isChecked;
        private string content;
        public object Config { get; set; }

        public bool IsChecked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public string Content
        {
            get { return content; }
            set { content = value; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public EditionItem(string content, object config)
        {
            Content = content;
            Config = config;
            IsChecked = false;
        }
    }
}
