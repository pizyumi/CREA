using System;
using System.Windows;
using System.Windows.Controls;

namespace CREA2014.Windows
{
    public partial class NewTransactionWindow : Window
    {
        private Func<string, bool> _IsValidAddress;
        private Func<object, CurrencyUnit> _GetBalance;

        public NewTransactionWindow(Action _UpdateDisplayedAccounts, Func<string, bool> __IsValidAddress, Func<object, CurrencyUnit> __GetBalance)
        {
            _IsValidAddress = __IsValidAddress;
            _GetBalance = __GetBalance;

            InitializeComponent();

            Title = "新しい取引".Multilanguage(231);
            tbAccountHolder.Text = "送付元口座名義".Multilanguage(232) + "：";
            rbAnonymous.Content = "匿名".Multilanguage(233) + "(_A)";
            rbPseudonymous.Content = "顕名".Multilanguage(234) + "(_P)";
            atAccount.Text = "送付元口座名".Multilanguage(235) + "(_B)：";
            tbBlanceLabel.Text = "使用可能残高".Multilanguage(236) + "：";
            tbBlanceUnit.Text = "CREA";
            atAccountTo.Text = "送付先口座番号".Multilanguage(237) + "(_T)：";
            atAmount.Text = "送付額".Multilanguage(238) + "(_M)：";
            tbAmountUnit.Text = "CREA";
            atFee.Text = "手数料".Multilanguage(239) + "(_F)：";
            tbFeeUnit.Text = "CREA";
            tbTotalLabel.Text = "計".Multilanguage(240) + "：";
            tbTotalUnit.Text = "CREA";
            bOK.Content = "OK".Multilanguage(241) + "(_O)";
            bCancel.Content = "キャンセル".Multilanguage(242) + "(_C)";

            rbAnonymous.Checked += (sender, e) => _UpdateDisplayedAccounts();
            rbPseudonymous.Checked += (sender, e) => _UpdateDisplayedAccounts();
            cbAccountHolder.SelectionChanged += (sender, e) => _UpdateDisplayedAccounts();

            PsudonymousAccountHolderValidate();
            AccountValidate();
            AddressValidate();
            AmountValidate();
            FeeValidate();
            TotalValidate();

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
            PsudonymousAccountHolderValidate();

            Validate();
        }

        private CurrencyUnit accountBalance = CurrencyUnit.Zero;

        private void cbAccount_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            AccountValidate();

            UpdateBalance();
        }

        private void tbAccountToAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            AddressValidate();

            Validate();
        }

        private void tbAmount_TextChanged(object sender, TextChangedEventArgs e)
        {
            AmountValidate();

            if (isValid4 && isValid5)
                tbTotal.Text = (decimal.Parse(tbAmount.Text) + decimal.Parse(tbFee.Text)).ToString();

            TotalValidate();

            Validate();
        }

        private void tbFee_TextChanged(object sender, TextChangedEventArgs e)
        {
            FeeValidate();

            if (isValid4 && isValid5)
                tbTotal.Text = (decimal.Parse(tbAmount.Text) + decimal.Parse(tbFee.Text)).ToString();

            TotalValidate();

            Validate();
        }

        private void SetIsEnabled(bool isEnabled)
        {
            cbAccountHolder.IsEnabled = isEnabled;
            tbAccountHolderChk.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        bool isValid1;
        bool isValid2;
        bool isValid3;
        bool isValid4;
        bool isValid5;
        bool isValid6;

        private void Validate()
        {
            bOK.IsEnabled = (rbAnonymous.IsChecked == true || isValid1) && isValid2 && isValid3 && isValid4 && isValid5 && isValid6;
        }

        private void PsudonymousAccountHolderValidate()
        {
            isValid1 = (cbAccountHolder.SelectedItem != null).Pipe((flag) =>
            {
                tbAccountHolderChk.Text = flag ? string.Empty : "口座名義を選択してください。".Multilanguage(243);
            });
        }

        private void AccountValidate()
        {
            isValid2 = (cbAccount.SelectedItem != null).Pipe((flag) =>
            {
                tbAccountChk.Text = flag ? string.Empty : "口座を選択してください。".Multilanguage(244);
            });
        }

        private void AddressValidate()
        {
            isValid3 = _IsValidAddress(tbAccountToAddress.Text).Pipe((flag) =>
            {
                tbAccountToChk.Text = flag ? string.Empty : "不正な口座番号です。".Multilanguage(261);
            });
        }

        private void AmountValidate()
        {
            decimal amount;
            isValid4 = decimal.TryParse(tbAmount.Text, out amount).Pipe((flag) =>
            {
                tbAmountChk.Text = flag ? string.Empty : "数値を入力してください。".Multilanguage(262);
            });
        }

        private void FeeValidate()
        {
            decimal fee;
            isValid5 = decimal.TryParse(tbFee.Text, out fee).Pipe((flag) =>
            {
                tbFeeChk.Text = flag ? string.Empty : "数値を入力してください。".Multilanguage(263);
            });
        }

        private void TotalValidate()
        {
            if (!isValid4 || !isValid5)
            {
                //無意味な場合はtrueということで
                isValid6 = true;

                tbTotalChk.Text = string.Empty;
            }
            else
            {
                CurrencyUnit amount = new Creacoin(decimal.Parse(tbAmount.Text));
                CurrencyUnit fee = new Creacoin(decimal.Parse(tbFee.Text));

                isValid6 = (accountBalance.rawAmount >= amount.rawAmount + fee.rawAmount).Pipe((flag) =>
                {
                    tbTotalChk.Text = flag ? string.Empty : "残高不足です。".Multilanguage(264);
                });
            }
        }

        public void UpdateBalance()
        {
            if (isValid2)
            {
                tbBlance.Text = (accountBalance = _GetBalance(cbAccount.SelectedItem)).AmountInCreacoin.Amount.ToString();

                if (isValid4 && isValid5)
                    TotalValidate();
            }

            Validate();
        }
    }
}