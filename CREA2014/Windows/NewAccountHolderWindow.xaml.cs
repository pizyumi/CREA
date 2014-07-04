using System.Windows;
using System.Windows.Controls;

namespace CREA2014.Windows
{
    public partial class NewAccountHolderWindow : Window
    {
        public NewAccountHolderWindow()
        {
            InitializeComponent();

            Title = "新しい口座名義".Multilanguage(52);
            atAccountHolder.Text = "口座名義".Multilanguage(53) + "(_H)：";
            bOK.Content = "OK".Multilanguage(58) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(59) + "(_C)";

            Validate();
        }

        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void tbAccountHolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            Validate();
        }

        private void Validate()
        {
            bOK.IsEnabled = tbAccountHolderValidate();
        }

        private bool tbAccountHolderValidate()
        {
            return (tbAccountHolder.Text != string.Empty).Operate((flag) =>
            {
                tbAccountHolderChk.Text = flag ? string.Empty : "口座名義は1文字以上の任意の文字列でなければなりません。".Multilanguage(139);
            });
        }
    }
}