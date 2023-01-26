using System.Data.Common;
using Pieces;
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
            this.MainBoard = new System.Windows.Forms.Panel();
            this.ConfirmMove = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // MainBoard
            // 
            this.MainBoard.Location = new System.Drawing.Point(16, 15);
            this.MainBoard.Name = "MainBoard";
            this.MainBoard.Size = new System.Drawing.Size(640, 640);
            this.MainBoard.TabIndex = 0;
            // 
            // ConfirmMove
            // 
            this.ConfirmMove.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(153)))), ((int)(((byte)(218)))), ((int)(((byte)(56)))));
            this.ConfirmMove.Location = new System.Drawing.Point(898, 157);
            this.ConfirmMove.Name = "ConfirmMove";
            this.ConfirmMove.Size = new System.Drawing.Size(322, 75);
            this.ConfirmMove.TabIndex = 1;
            this.ConfirmMove.Text = "Confirm Selection";
            this.ConfirmMove.UseVisualStyleBackColor = false;
            this.ConfirmMove.Visible = false;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.SaddleBrown;
            this.ClientSize = new System.Drawing.Size(1332, 686);
            this.Controls.Add(this.ConfirmMove);
            this.Controls.Add(this.MainBoard);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);

        }

        #endregion

        private Button ConfirmMove;
        private Panel MainBoard;

        private Label[,] pictureSquares;
        private GameEnvironment _currentGame;
        private List<Label> _validSquares = new List<Label>();

        private Label? _targetSquare = null;
        private Label? _friendlySelectedSquare = null;

        private Player playerOne;
        private Player playerTwo;

        private Player _activePlayer;

        private List<OriginalBackColor> _changedSquares = new List<OriginalBackColor>();
        private List<MovementInformation> _movesAvailableToPiece;
        private string _targetText;
        private string _friendlyText;

        private bool _resetSquareAssignments;
        
    }
}