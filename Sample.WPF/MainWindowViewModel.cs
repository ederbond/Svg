using Reactive.Bindings;

namespace Samples.Wpf
{
    public class MainWindowViewModel
    {
        public ReactiveProperty<string> ColorMapping { get; }
        public ReactiveCommand<string> ChangeColorCommand { get; }

        public MainWindowViewModel()
        {
            ColorMapping = new ReactiveProperty<string>();
            ChangeColorCommand = new ReactiveCommand<string>().WithSubscribe(OnChangeColor);
        }

        private void OnChangeColor(string colorMap)
        {
            ColorMapping.Value = colorMap;
        }
    }
}
