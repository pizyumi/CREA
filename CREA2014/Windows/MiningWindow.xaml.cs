using System;
using System.Windows;

namespace CREA2014.Windows
{
    public partial class MiningWindow : Window
    {
        public MiningWindow(Action<Window> _NewAccountHolder, Action<Window> _NewAccount, Action _UpdateDisplayedAccounts)
        {
            InitializeComponent();

            Title = "採掘開始".Multilanguage(157);
            tbMiningDescription.Text = "ブロック報酬を送付する口座を選択してください。".Multilanguage(158);
            tbAccountHolder.Text = "口座名義".Multilanguage(159) + "：";
            rbAnonymous.Content = "匿名".Multilanguage(160) + "(_A)";
            rbPseudonymous.Content = "顕名".Multilanguage(161) + "(_P)";
            bNewAccountHolder.Content = "新しい口座名義".Multilanguage(162) + "(_H)...";
            atAccount.Text = "口座名".Multilanguage(163) + "(_A)：";
            bNewAccount.Content = "新しい口座".Multilanguage(164) + "(_I)...";
            bOK.Content = "OK".Multilanguage(165) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(166) + "(_C)";

            bNewAccountHolder.Click += (sender, e) => _NewAccountHolder(this);
            bNewAccount.Click += (sender, e) => _NewAccount(this);
            rbAnonymous.Checked += (sender, e) => _UpdateDisplayedAccounts();
            rbPseudonymous.Checked += (sender, e) => _UpdateDisplayedAccounts();
            cbAccountHolder.SelectionChanged += (sender, e) => _UpdateDisplayedAccounts();

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

        private void cbAccount_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
            bool isValid1 = PsudonymousAccountHolderValidate();
            bool isValid2 = AccountValidate();

            bOK.IsEnabled = (rbAnonymous.IsChecked == true || isValid1) && isValid2;
        }

        private bool PsudonymousAccountHolderValidate()
        {
            return (cbAccountHolder.SelectedItem != null).Pipe((flag) =>
            {
                tbAccountHolderChk.Text = flag ? string.Empty : "口座名義を選択してください。".Multilanguage(167);
            });
        }

        private bool AccountValidate()
        {
            return (cbAccount.SelectedItem != null).Pipe((flag) =>
            {
                tbAccountChk.Text = flag ? string.Empty : "口座を選択してください。".Multilanguage(168);
            });
        }
    }
}