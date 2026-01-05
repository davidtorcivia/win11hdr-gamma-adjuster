using System.Windows.Input;

namespace HDRGammaController.ViewModels
{
    public class ActionViewModel
    {
        public string Header { get; }
        public ICommand? Command { get; }
        public bool IsSeparator { get; }

        public ActionViewModel(string header, ICommand? command)
        {
            Header = header;
            Command = command;
            IsSeparator = false;
        }

        public System.Collections.IEnumerable? SubItems => null;

        public ActionViewModel(bool isSeparator)
        {
            IsSeparator = true;
            Header = string.Empty;
            Command = null!;
        }
    }
}
