
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
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.StartServer = new System.Windows.Forms.Button();
            this.JoinServer = new System.Windows.Forms.Button();
            this.MainView = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(187, 688);
            this.flowLayoutPanel1.TabIndex = 0;
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
            // 
            // JoinServer
            // 
            this.JoinServer.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(34)))), ((int)(((byte)(82)))), ((int)(((byte)(57)))));
            this.JoinServer.ForeColor = System.Drawing.Color.White;
            this.JoinServer.Location = new System.Drawing.Point(0, 50);
            this.JoinServer.Name = "JoinServer";
            this.JoinServer.Size = new System.Drawing.Size(187, 50);
            this.JoinServer.TabIndex = 2;
            this.JoinServer.Text = "Join Server";
            this.JoinServer.UseVisualStyleBackColor = false;
            // 
            // MainView
            // 
            this.MainView.Location = new System.Drawing.Point(187, 0);
            this.MainView.Name = "MainView";
            this.MainView.Size = new System.Drawing.Size(1040, 688);
            this.MainView.TabIndex = 3;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(39)))), ((int)(((byte)(46)))), ((int)(((byte)(68)))));
            this.ClientSize = new System.Drawing.Size(1432, 686);
            this.Controls.Add(this.MainView);
            this.Controls.Add(this.JoinServer);
            this.Controls.Add(this.StartServer);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Name = "Form1";
            this.Text = "Chess";
            this.ResumeLayout(false);

        }



        #endregion

        private FlowLayoutPanel flowLayoutPanel1;
        private Button StartServer;
        private Button JoinServer;
        private Panel MainView;
    }
}