using System;
using System.Windows;
using System.Windows.Controls;

namespace CREA2014.Windows
{
    public partial class TransferWindow : Window
    {
        public TransferWindow(Action _UpdateDisplayedAccounts)
        {
            InitializeComponent();

            Title = "新しい取引".Multilanguage(231);
            tbAccountHolder.Text = "送付元口座名義".Multilanguage(232) + "：";
            rbAnonymous.Content = "匿名".Multilanguage(233) + "(_A)";
            rbPseudonymous.Content = "顕名".Multilanguage(234) + "(_P)";
            atAccount.Text = "送付元口座名".Multilanguage(235) + "(_B)：";
            tbBlanceLabel.Text = "残高".Multilanguage(236) + "：";
            tbBlanceUnit.Text = "CREA";
            atAccountTo.Text = "送付先口座番号".Multilanguage(237) + "(_T)：";
            atAmmount.Text = "送付額".Multilanguage(238) + "(_M)：";
            tbAmmountUnit.Text = "CREA";
            atFee.Text = "手数料".Multilanguage(239) + "(_F)：";
            tbFeeUnit.Text = "CREA";
            tbTotalLabel.Text = "計".Multilanguage(240) + "：";
            tbTotalUnit.Text = "CREA";
            bOK.Content = "OK".Multilanguage(241) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(242) + "(_C)";

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

        private void cbAccountHolder_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Validate();
        }

        private void cbAccount_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Validate();
        }

        private void SetIsEnabled(bool isEnabled)
        {
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
                tbAccountHolderChk.Text = flag ? string.Empty : "口座名義を選択してください。".Multilanguage(243);
            });
        }

        private bool AccountValidate()
        {
            return (cbAccount.SelectedItem != null).Pipe((flag) =>
            {
                tbAccountChk.Text = flag ? string.Empty : "口座を選択してください。".Multilanguage(244);
            });
        }
    }
}