namespace RustPlus_Toolbox
{
    partial class MainWindow
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _oled?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lblServerName = new Label();
            lblServerTime = new Label();
            lblNumberOfPlayers = new Label();
            flowLayoutPanel2 = new FlowLayoutPanel();
            SuspendLayout();
            // 
            // lblServerName
            // 
            lblServerName.AutoSize = true;
            lblServerName.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblServerName.Location = new Point(12, 9);
            lblServerName.Name = "lblServerName";
            lblServerName.Size = new Size(84, 15);
            lblServerName.TabIndex = 1;
            lblServerName.Text = "lblServerName";
            // 
            // lblServerTime
            // 
            lblServerTime.AutoSize = true;
            lblServerTime.Font = new Font("Segoe UI", 48F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblServerTime.ImageAlign = ContentAlignment.TopLeft;
            lblServerTime.Location = new Point(-1, 24);
            lblServerTime.Margin = new Padding(0);
            lblServerTime.Name = "lblServerTime";
            lblServerTime.Size = new Size(368, 86);
            lblServerTime.TabIndex = 2;
            lblServerTime.Text = "Server Time";
            // 
            // lblNumberOfPlayers
            // 
            lblNumberOfPlayers.AutoSize = true;
            lblNumberOfPlayers.Location = new Point(12, 110);
            lblNumberOfPlayers.Name = "lblNumberOfPlayers";
            lblNumberOfPlayers.Size = new Size(114, 15);
            lblNumberOfPlayers.TabIndex = 3;
            lblNumberOfPlayers.Text = "lblNumberOfPlayers";
            // 
            // flowLayoutPanel2
            // 
            flowLayoutPanel2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            flowLayoutPanel2.AutoScroll = true;
            flowLayoutPanel2.Location = new Point(0, 146);
            flowLayoutPanel2.Name = "flowLayoutPanel2";
            flowLayoutPanel2.Padding = new Padding(12);
            flowLayoutPanel2.Size = new Size(419, 0);
            flowLayoutPanel2.TabIndex = 5;
            // 
            // MainWindow
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(419, 134);
            Controls.Add(flowLayoutPanel2);
            Controls.Add(lblNumberOfPlayers);
            Controls.Add(lblServerTime);
            Controls.Add(lblServerName);
            Name = "MainWindow";
            Text = "RustPlus Toolbox";
            Load += MainWindow_LoadAsync;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label lblServerName;
        private Label lblServerTime;
        private Label lblNumberOfPlayers;
        private FlowLayoutPanel flowLayoutPanel2;
    }
}
