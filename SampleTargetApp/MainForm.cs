using System;
using System.Drawing;
using System.Windows.Forms;

namespace SampleTargetApp
{
    public class MainForm : Form
    {
        private static readonly Color HeaderColor = Color.FromArgb(0x15, 0x65, 0xC0);
        private static readonly Color SuccessColor = Color.FromArgb(0x2E, 0x7D, 0x32);
        private static readonly Color BackgroundColor = Color.FromArgb(0xF5, 0xF5, 0xF5);
        private static readonly Color MutedColor = Color.FromArgb(0x75, 0x75, 0x75);

        private readonly Font _bodyFont = new Font("Segoe UI", 10F);
        private readonly Font _labelFont = new Font("Segoe UI", 9.5F);

        private readonly TabControl _tabs = new TabControl { Name = "tabMain" };

        private readonly TextBox _fullNameTextBox = new TextBox { Name = "txtFullName" };
        private readonly TextBox _emailTextBox = new TextBox { Name = "txtEmail" };
        private readonly TextBox _makeTextBox = new TextBox { Name = "txtMake" };
        private readonly TextBox _modelTextBox = new TextBox { Name = "txtModel" };
        private readonly CheckBox _roadsideCheckBox = new CheckBox { Name = "chkRoadside" };
        private readonly Button _submitButton = new Button { Name = "btnSubmit" };
        private readonly Label _submitErrorLabel = new Label { Name = "lblSubmitError" };

        // Intentionally never assigned a Name: WinForms derives an element's UI Automation
        // AutomationId from Control.Name, so leaving this unset exercises the automation
        // tool's fallback locator strategy (ControlType + Name + sibling index) end-to-end.
        private readonly Label _confirmationLabel = new Label();

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Sample Insurance Application";
            ClientSize = new Size(560, 460);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = BackgroundColor;
            Font = _bodyFont;

            var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = HeaderColor };
            var headerLabel = new Label
            {
                Text = "Sample Insurance Application",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 16)
            };
            header.Controls.Add(headerLabel);

            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = _bodyFont;
            _tabs.TabPages.Add(BuildPersonalInfoTab());
            _tabs.TabPages.Add(BuildVehicleDetailsTab());
            _tabs.TabPages.Add(BuildCoverageTab());

            Controls.Add(_tabs);
            Controls.Add(header);
        }

        private TabPage BuildPersonalInfoTab()
        {
            var page = new TabPage("Personal Information") { Name = "tabPersonalInfo", BackColor = BackgroundColor };

            var nameLabel = new Label { Text = "Full Name", Font = _labelFont, ForeColor = MutedColor, Location = new Point(24, 24), AutoSize = true };
            _fullNameTextBox.Font = _bodyFont;
            _fullNameTextBox.Location = new Point(24, 46);
            _fullNameTextBox.Width = 460;
            _fullNameTextBox.BorderStyle = BorderStyle.FixedSingle;

            var emailLabel = new Label { Text = "Email", Font = _labelFont, ForeColor = MutedColor, Location = new Point(24, 86), AutoSize = true };
            _emailTextBox.Font = _bodyFont;
            _emailTextBox.Location = new Point(24, 108);
            _emailTextBox.Width = 460;
            _emailTextBox.BorderStyle = BorderStyle.FixedSingle;

            page.Controls.Add(nameLabel);
            page.Controls.Add(_fullNameTextBox);
            page.Controls.Add(emailLabel);
            page.Controls.Add(_emailTextBox);
            return page;
        }

        private TabPage BuildVehicleDetailsTab()
        {
            var page = new TabPage("Vehicle Details") { Name = "tabVehicleDetails", BackColor = BackgroundColor };

            var makeLabel = new Label { Text = "Make", Font = _labelFont, ForeColor = MutedColor, Location = new Point(24, 24), AutoSize = true };
            _makeTextBox.Font = _bodyFont;
            _makeTextBox.Location = new Point(24, 46);
            _makeTextBox.Width = 460;
            _makeTextBox.BorderStyle = BorderStyle.FixedSingle;

            var modelLabel = new Label { Text = "Model", Font = _labelFont, ForeColor = MutedColor, Location = new Point(24, 86), AutoSize = true };
            _modelTextBox.Font = _bodyFont;
            _modelTextBox.Location = new Point(24, 108);
            _modelTextBox.Width = 460;
            _modelTextBox.BorderStyle = BorderStyle.FixedSingle;

            page.Controls.Add(makeLabel);
            page.Controls.Add(_makeTextBox);
            page.Controls.Add(modelLabel);
            page.Controls.Add(_modelTextBox);
            return page;
        }

        private TabPage BuildCoverageTab()
        {
            var page = new TabPage("Coverage && Review") { Name = "tabCoverageReview", BackColor = BackgroundColor };

            _roadsideCheckBox.Text = "Roadside Assistance";
            _roadsideCheckBox.Font = _labelFont;
            _roadsideCheckBox.ForeColor = Color.Black;
            _roadsideCheckBox.Location = new Point(24, 24);
            _roadsideCheckBox.AutoSize = true;

            _submitButton.Text = "Submit Application";
            _submitButton.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _submitButton.Location = new Point(24, 64);
            _submitButton.Size = new Size(200, 40);
            _submitButton.FlatStyle = FlatStyle.Flat;
            _submitButton.FlatAppearance.BorderSize = 0;
            _submitButton.BackColor = HeaderColor;
            _submitButton.ForeColor = Color.White;
            _submitButton.Click += OnSubmitClicked;

            _submitErrorLabel.Text = string.Empty;
            _submitErrorLabel.Font = _labelFont;
            _submitErrorLabel.ForeColor = Color.Firebrick;
            _submitErrorLabel.Location = new Point(24, 116);
            _submitErrorLabel.AutoSize = true;

            _confirmationLabel.AutoSize = true;
            _confirmationLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _confirmationLabel.ForeColor = SuccessColor;
            _confirmationLabel.Location = new Point(24, 116);

            page.Controls.Add(_roadsideCheckBox);
            page.Controls.Add(_submitButton);
            page.Controls.Add(_submitErrorLabel);
            page.Controls.Add(_confirmationLabel);
            return page;
        }

        private void OnSubmitClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_fullNameTextBox.Text) ||
                string.IsNullOrWhiteSpace(_emailTextBox.Text) ||
                string.IsNullOrWhiteSpace(_makeTextBox.Text) ||
                string.IsNullOrWhiteSpace(_modelTextBox.Text))
            {
                _submitErrorLabel.Text = "Please fill in your name, email, make, and model before submitting.";
                _confirmationLabel.Text = string.Empty;
                return;
            }

            _submitErrorLabel.Text = string.Empty;
            // Deliberately not string.GetHashCode(): .NET randomizes string hash codes per
            // process by default, so a hash-derived reference would differ on every run and
            // make this confirmation text impossible to assert on reliably in a replayed test.
            var reference = ComputeStableReference(_fullNameTextBox.Text, _emailTextBox.Text);
            _confirmationLabel.Text = $"Application submitted! Reference #{reference}";
        }

        private static string ComputeStableReference(string fullName, string email)
        {
            var seed = 0;
            foreach (var c in fullName + "|" + email)
            {
                seed = seed * 31 + c;
            }

            return (Math.Abs(seed) % 100000).ToString("D5");
        }
    }
}
