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
            atName.Text = "口座名".Multilanguage(67) + "(_A)：";
            atDescription.Text = "説明".Multilanguage(68) + "(_D)：";
            bOK.Content = "OK".Multilanguage(73) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(74) + "(_C)";

            bNewAccountHolder.Click += (sender, e) => _NewAccountHolder(this);

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

        private void rbAnonymous_Checked(object sender, RoutedEventArgs e)
        {
            SetIsEnabled(false);

            Validate();
        }

        private void rbPseudonymous_Checked(object sender, RoutedEventArgs e)
        {
            SetIsEnabled(true);

            Validate();
        }

        private void cbAccountHolder_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Validate();
        }

        private void tbName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            Validate();
        }

        private void SetIsEnabled(bool isEnabled)
        {
            bNewAccountHolder.IsEnabled = isEnabled;
            cbAccountHolder.IsEnabled = isEnabled;
            tbAccountHolderChk.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Validate()
        {
            bool isValid1 = tbNameValidate();
            bool isValid2 = PsudonymousAccountHolderValidate();

            bOK.IsEnabled = isValid1 && (rbAnonymous.IsChecked == true || isValid2);
        }

        private bool tbNameValidate()
        {
            return (tbName.Text != string.Empty).Operate((flag) =>
            {
                tbNameChk.Text = flag ? string.Empty : "口座名は1文字以上の任意の文字列でなければなりません。".Multilanguage(140);
            });
        }

        private bool PsudonymousAccountHolderValidate()
        {
            return (cbAccountHolder.SelectedItem != null).Operate((flag) =>
            {
                tbAccountHolderChk.Text = flag ? string.Empty : "口座名義を選択してください。".Multilanguage(141);
            });
        }
    }
}