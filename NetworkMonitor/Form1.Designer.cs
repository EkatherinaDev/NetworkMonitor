namespace NetworkMonitor;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private TableLayoutPanel rootLayout;
    private TableLayoutPanel headerLayout;
    private Label titleLabel;
    private Label lastUpdateLabel;
    private FlowLayoutPanel actionPanel;
    private Label ipInputLabel;
    private TextBox ipTextBox;
    private Button addIpButton;
    private Button checkIpButton;
    private Button checkSelectedButton;
    private Button scanNetworkButton;
    private DataGridView devicesGrid;
    private GroupBox eventLogGroupBox;
    private ListBox eventLogListBox;
    private TableLayoutPanel footerLayout;
    private Label statusLabel;
    private ProgressBar scanProgressBar;
    private System.Windows.Forms.Timer autoScanTimer;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
            _serviceCancellation?.Cancel();
            _serviceCancellation?.Dispose();
            _serverRowFont?.Dispose();
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.rootLayout = new TableLayoutPanel();
        this.headerLayout = new TableLayoutPanel();
        this.titleLabel = new Label();
        this.lastUpdateLabel = new Label();
        this.actionPanel = new FlowLayoutPanel();
        this.ipInputLabel = new Label();
        this.ipTextBox = new TextBox();
        this.addIpButton = new Button();
        this.checkIpButton = new Button();
        this.checkSelectedButton = new Button();
        this.scanNetworkButton = new Button();
        this.devicesGrid = new DataGridView();
        this.eventLogGroupBox = new GroupBox();
        this.eventLogListBox = new ListBox();
        this.footerLayout = new TableLayoutPanel();
        this.statusLabel = new Label();
        this.scanProgressBar = new ProgressBar();
        this.autoScanTimer = new System.Windows.Forms.Timer(this.components);
        this.rootLayout.SuspendLayout();
        this.headerLayout.SuspendLayout();
        this.actionPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.devicesGrid)).BeginInit();
        this.eventLogGroupBox.SuspendLayout();
        this.footerLayout.SuspendLayout();
        this.SuspendLayout();
        // 
        // rootLayout
        // 
        this.rootLayout.ColumnCount = 1;
        this.rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        this.rootLayout.Controls.Add(this.headerLayout, 0, 0);
        this.rootLayout.Controls.Add(this.actionPanel, 0, 1);
        this.rootLayout.Controls.Add(this.devicesGrid, 0, 2);
        this.rootLayout.Controls.Add(this.eventLogGroupBox, 0, 3);
        this.rootLayout.Controls.Add(this.footerLayout, 0, 4);
        this.rootLayout.Dock = DockStyle.Fill;
        this.rootLayout.Padding = new Padding(10);
        this.rootLayout.RowCount = 5;
        this.rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        this.rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
        this.rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 
        // headerLayout
        // 
        this.headerLayout.AutoSize = true;
        this.headerLayout.ColumnCount = 2;
        this.headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        this.headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        this.headerLayout.Controls.Add(this.titleLabel, 0, 0);
        this.headerLayout.Controls.Add(this.lastUpdateLabel, 1, 0);
        this.headerLayout.Dock = DockStyle.Fill;
        this.headerLayout.Margin = new Padding(0, 0, 0, 8);
        this.headerLayout.RowCount = 1;
        this.headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 
        // titleLabel
        // 
        this.titleLabel.AutoSize = true;
        this.titleLabel.Dock = DockStyle.Fill;
        this.titleLabel.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        this.titleLabel.Text = "Мониторинг сети";
        // 
        // lastUpdateLabel
        // 
        this.lastUpdateLabel.AutoSize = true;
        this.lastUpdateLabel.Dock = DockStyle.Fill;
        this.lastUpdateLabel.Font = new Font("Segoe UI", 10F, FontStyle.Italic);
        this.lastUpdateLabel.ForeColor = Color.DimGray;
        this.lastUpdateLabel.Text = "Последнее обновление: нет данных";
        this.lastUpdateLabel.TextAlign = ContentAlignment.MiddleRight;
        // 
        // actionPanel
        // 
        this.actionPanel.AutoSize = true;
        this.actionPanel.Controls.Add(this.ipInputLabel);
        this.actionPanel.Controls.Add(this.ipTextBox);
        this.actionPanel.Controls.Add(this.addIpButton);
        this.actionPanel.Controls.Add(this.checkIpButton);
        this.actionPanel.Controls.Add(this.checkSelectedButton);
        this.actionPanel.Controls.Add(this.scanNetworkButton);
        this.actionPanel.Dock = DockStyle.Fill;
        this.actionPanel.Margin = new Padding(0, 0, 0, 8);
        this.actionPanel.WrapContents = true;
        // 
        // ipInputLabel
        // 
        this.ipInputLabel.AutoSize = true;
        this.ipInputLabel.Margin = new Padding(0, 7, 8, 0);
        this.ipInputLabel.Text = "IP-адрес:";
        // 
        // ipTextBox
        // 
        this.ipTextBox.Margin = new Padding(0, 3, 8, 3);
        this.ipTextBox.PlaceholderText = "192.168.1.10";
        this.ipTextBox.Size = new Size(150, 27);
        // 
        // addIpButton
        // 
        this.addIpButton.AutoSize = true;
        this.addIpButton.Margin = new Padding(0, 2, 8, 2);
        this.addIpButton.Text = "Добавить";
        this.addIpButton.UseVisualStyleBackColor = true;
        this.addIpButton.Click += this.addIpButton_Click;
        // 
        // checkIpButton
        // 
        this.checkIpButton.AutoSize = true;
        this.checkIpButton.Margin = new Padding(0, 2, 8, 2);
        this.checkIpButton.Text = "Проверить IP";
        this.checkIpButton.UseVisualStyleBackColor = true;
        this.checkIpButton.Click += this.checkIpButton_Click;
        // 
        // checkSelectedButton
        // 
        this.checkSelectedButton.AutoSize = true;
        this.checkSelectedButton.Margin = new Padding(0, 2, 8, 2);
        this.checkSelectedButton.Text = "Проверить выбранный";
        this.checkSelectedButton.UseVisualStyleBackColor = true;
        this.checkSelectedButton.Click += this.checkSelectedButton_Click;
        // 
        // scanNetworkButton
        // 
        this.scanNetworkButton.AutoSize = true;
        this.scanNetworkButton.Margin = new Padding(0, 2, 0, 2);
        this.scanNetworkButton.Text = "Сканировать всю сеть";
        this.scanNetworkButton.UseVisualStyleBackColor = true;
        this.scanNetworkButton.Click += this.scanNetworkButton_Click;
        // 
        // devicesGrid
        // 
        this.devicesGrid.AllowUserToAddRows = false;
        this.devicesGrid.AllowUserToDeleteRows = false;
        this.devicesGrid.AllowUserToResizeRows = false;
        this.devicesGrid.BackgroundColor = SystemColors.Window;
        this.devicesGrid.BorderStyle = BorderStyle.Fixed3D;
        this.devicesGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        this.devicesGrid.Dock = DockStyle.Fill;
        this.devicesGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        this.devicesGrid.Location = new Point(10, 91);
        this.devicesGrid.Margin = new Padding(0);
        this.devicesGrid.MultiSelect = false;
        this.devicesGrid.ReadOnly = true;
        this.devicesGrid.RowHeadersWidth = 48;
        this.devicesGrid.RowTemplate.Height = 30;
        this.devicesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.devicesGrid.CellDoubleClick += this.devicesGrid_CellDoubleClick;
        //
        // eventLogGroupBox
        //
        this.eventLogGroupBox.Controls.Add(this.eventLogListBox);
        this.eventLogGroupBox.Dock = DockStyle.Fill;
        this.eventLogGroupBox.Margin = new Padding(0, 8, 0, 0);
        this.eventLogGroupBox.Text = "Журнал событий мониторинга";
        //
        // eventLogListBox
        //
        this.eventLogListBox.BorderStyle = BorderStyle.None;
        this.eventLogListBox.Dock = DockStyle.Fill;
        this.eventLogListBox.Font = new Font("Consolas", 9F);
        this.eventLogListBox.FormattingEnabled = true;
        this.eventLogListBox.HorizontalScrollbar = true;
        this.eventLogListBox.IntegralHeight = false;
        this.eventLogListBox.ItemHeight = 18;
        this.eventLogListBox.Margin = new Padding(8);
        //
        // footerLayout
        // 
        this.footerLayout.AutoSize = true;
        this.footerLayout.ColumnCount = 2;
        this.footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        this.footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230F));
        this.footerLayout.Controls.Add(this.statusLabel, 0, 0);
        this.footerLayout.Controls.Add(this.scanProgressBar, 1, 0);
        this.footerLayout.Dock = DockStyle.Fill;
        this.footerLayout.Margin = new Padding(0, 8, 0, 0);
        this.footerLayout.RowCount = 1;
        this.footerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 
        // statusLabel
        // 
        this.statusLabel.AutoSize = true;
        this.statusLabel.Dock = DockStyle.Fill;
        this.statusLabel.ForeColor = Color.DimGray;
        this.statusLabel.Text = "Готово.";
        this.statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // scanProgressBar
        // 
        this.scanProgressBar.Dock = DockStyle.Fill;
        this.scanProgressBar.Margin = new Padding(8, 2, 0, 2);
        this.scanProgressBar.Visible = false;
        // 
        // Form1
        // 
        this.AutoScaleDimensions = new SizeF(8F, 20F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(1120, 760);
        this.Controls.Add(this.rootLayout);
        this.Font = new Font("Segoe UI", 10F);
        this.MinimumSize = new Size(900, 620);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Text = "Мониторинг сети";
        this.rootLayout.ResumeLayout(false);
        this.rootLayout.PerformLayout();
        this.headerLayout.ResumeLayout(false);
        this.headerLayout.PerformLayout();
        this.actionPanel.ResumeLayout(false);
        this.actionPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.devicesGrid)).EndInit();
        this.eventLogGroupBox.ResumeLayout(false);
        this.footerLayout.ResumeLayout(false);
        this.footerLayout.PerformLayout();
        this.ResumeLayout(false);
    }

    #endregion
}
