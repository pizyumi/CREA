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
            tbKeyLength.Text = "鍵長".Multilanguage(54) + "：";
            rb256bit.Content = "256ビット".Multilanguage(55) + "(_2)";
            rb384bit.Content = "384ビット".Multilanguage(56) + "(_3)";
            rb521bit.Content = "521ビット".Multilanguage(57) + "(_5)";
            bOK.Content = "OK".Multilanguage(58) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(59) + "(_C)";

            rb256bit.IsChecked = true;
        }

        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}