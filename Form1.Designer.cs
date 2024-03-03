
namespace Chess_GUi
{
    partial class Form1
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
            if (disposing && (components != null))
            {
                components.Dispose();
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
            this.JoinServer = new System.Windows.Forms.Button();
            this.StartServer = new System.Windows.Forms.Button();
            this.MainView = new System.Windows.Forms.Panel();
            this.panel1 = new System.Windows.Forms.Panel();
            this.GameTracker = new System.Windows.Forms.ListBox();
            this.UserName = new System.Windows.Forms.TextBox();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // JoinServer
            // 
            this.JoinServer.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(34)))), ((int)(((byte)(82)))), ((int)(((byte)(57)))));
            this.JoinServer.ForeColor = System.Drawing.Color.White;
            this.JoinServer.Location = new System.Drawing.Point(0, 55);
            this.JoinServer.Name = "JoinServer";
            this.JoinServer.Size = new System.Drawing.Size(187, 50);
            this.JoinServer.TabIndex = 2;
            this.JoinServer.Text = "Join Server";
            this.JoinServer.UseVisualStyleBackColor = false;
            this.JoinServer.Click += new System.EventHandler(this.JoinServer_Click);
            // 
            // StartServer
            // 
            this.StartServer.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(34)))), ((int)(((byte)(82)))), ((int)(((byte)(57)))));
            this.StartServer.ForeColor = System.Drawing.Color.White;
            this.StartServer.Location = new System.Drawing.Point(0, 0);
            this.StartServer.Name = "StartServer";
            this.StartServer.Size = new System.Drawing.Size(187, 50);
            this.StartServer.TabIndex = 1;
            this.StartServer.Text = "Start Server";
            this.StartServer.UseVisualStyleBackColor = false;
            this.StartServer.Click += new System.EventHandler(this.StartServer_Click);
            // 
            // MainView
            // 
            this.MainView.BackColor = System.Drawing.Color.DarkGray;
            this.MainView.Location = new System.Drawing.Point(191, 0);
            this.MainView.Name = "MainView";
            this.MainView.Size = new System.Drawing.Size(1040, 685);
            this.MainView.TabIndex = 3;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.GameTracker);
            this.panel1.Controls.Add(this.UserName);
            this.panel1.Controls.Add(this.StartServer);
            this.panel1.Controls.Add(this.JoinServer);
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(190, 685);
            this.panel1.TabIndex = 4;
            // 
            // GameTracker
            // 
            this.GameTracker.FormattingEnabled = true;
            this.GameTracker.ItemHeight = 20;
            this.GameTracker.Location = new System.Drawing.Point(0, 244);
            this.GameTracker.Name = "GameTracker";
            this.GameTracker.Size = new System.Drawing.Size(187, 284);
            this.GameTracker.TabIndex = 4;
            this.GameTracker.SelectedIndexChanged += new System.EventHandler(this.GameTracker_SelectedIndexChanged);
            // 
            // UserName
            // 
            this.UserName.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(224)))), ((int)(((byte)(192)))));
            this.UserName.Location = new System.Drawing.Point(0, 120);
            this.UserName.Name = "UserName";
            this.UserName.Size = new System.Drawing.Size(187, 27);
            this.UserName.TabIndex = 3;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(39)))), ((int)(((byte)(46)))), ((int)(((byte)(68)))));
            this.ClientSize = new System.Drawing.Size(1234, 686);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.MainView);
            this.Name = "Form1";
            this.Text = "Chess";
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }
        #endregion
        private Button StartServer;
        private Button JoinServer;
        private Panel MainView;
        private Panel panel1;
        private TextBox UserName;
        private ListBox GameTracker;
    }
}