using System.Windows.Markup;

namespace GMentor.Services
{
    [MarkupExtensionReturnType(typeof(string))]
    public class L : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public L() { }

        public L(string key) => Key = key;

        public override object ProvideValue(IServiceProvider serviceProvider)
            => LocalizationService.T(Key);
    }
}
