using System;
using System.Windows;

namespace CREA2014.Windows
{
    public partial class NewAccountWindow : Window
    {
        public NewAccountWindow(Action<Window> _NewAccountHolder)
        {
            InitializeComponent();

            Title = "新しい口座".Multilanguage(62);
            tbAccountHolder.Text = "口座名義".Multilanguage(63) + "：";
            rbAnonymous.Content = "匿名".Multilanguage(64) + "(_A)";
            rbPseudonymous.Content = "顕名".Multilanguage(65) + "(_P)";
            bNewAccountHolder.Content = "新しい口座名義".Multilanguage(66) + "(_H)...";
            atName.Text = "口座名".Multilanguage(67) + "(_O)：";
            atDescription.Text = "説明".Multilanguage(68) + "(_D)：";
            tbKeyLength.Text = "鍵長".Multilanguage(69) + "：";
            rb256bit.Content = "256ビット".Multilanguage(70) + "(_2)";
            rb384bit.Content = "384ビット".Multilanguage(71) + "(_3)";
            rb521bit.Content = "521ビット".Multilanguage(72) + "(_5)";
            bOK.Content = "OK".Multilanguage(73) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(74) + "(_C)";

            rbAnonymous.IsChecked = true;
            rb256bit.IsChecked = true;

            bNewAccountHolder.Click += (sender, e) => _NewAccountHolder(this);
        }

        private void bOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void rbAnonymous_Checked(object sender, RoutedEventArgs e)
        {
            SetIsEnabled(false);
            SetbOKIsEnabled();
        }

        private void rbPseudonymous_Checked(object sender, RoutedEventArgs e)
        {
            SetIsEnabled(true);
            SetbOKIsEnabled();
        }

        private void cbAccountHolder_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SetbOKIsEnabled();
        }

        private bool IsOk
        {
            get { return rbAnonymous.IsChecked == true || cbAccountHolder.SelectedItem != null; }
        }

        private void SetbOKIsEnabled()
        {
            bOK.IsEnabled = IsOk;
        }

        private void SetIsEnabled(bool isEnabled)
        {
            bNewAccountHolder.IsEnabled = isEnabled;
            cbAccountHolder.IsEnabled = isEnabled;
        }
    }
}