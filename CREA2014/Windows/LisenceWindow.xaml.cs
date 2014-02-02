using System.IO;
using System.Windows;

namespace CREA2014.Windows
{
    public partial class LisenceWindow : Window
    {
        public LisenceWindow(string _contractText)
        {
            InitializeComponent();

            Title = "ソフトウェア使用許諾契約書".Multilanguage(15);
            tbDescription.Text = "以下のソフトウェア使用許諾契約書を通読し、同意する場合には「同意する」を、同意しない場合には「同意しない」を選択してください。同意しない場合にはCREAを使用することはできません。そのため、「同意しない」を選択した場合には、CREAは即刻終了します。".Multilanguage(16);
            tLisence.Text = _contractText;
            bAgree.Content = "同意する".Multilanguage(17) + "(_A)";
            bDisagree.Content = "同意しない".Multilanguage(18) + "(_D)";
        }

        private void bAgree_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void bDisagree_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}