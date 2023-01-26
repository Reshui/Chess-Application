using Pieces;
using System.Numerics;

namespace Chess_GUi
{
    public readonly struct OriginalBackColor
    {
        public OriginalBackColor(Label square)
        {
            SquareChanged = square;
            OriginalColor = square.BackColor;
            ForeColor = square.ForeColor;
        }
        public Label SquareChanged { get; init; }
        public Color OriginalColor { get; init; }
        public Color ForeColor { get; init; }

    }
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            playerOne = new Player();
            playerTwo = new Player();
            _currentGame = new GameEnvironment(playerOne, playerTwo);

            _activePlayer = _currentGame.WhitePlayer;
            ConfirmMove.Click += new EventHandler(this.ConfirmMoveClickedEvent!);
            CreateBoard();
        }

        private void CreateBoard()
        {
            MainBoard.Parent = this;

            int squaresToCreate = _currentGame.GameBoard.GetUpperBound(0) + 1;

            pictureSquares = new Label[squaresToCreate, squaresToCreate];

            int squareLength = MainBoard.Width / squaresToCreate;

            Color blackColor = ColorTranslator.FromHtml("#300d21");
            Color whiteColor = Color.MistyRose;

            // Initialize as whiteColor so that the fisrt square made will be blackColor.
            Color squareColor = whiteColor;
            int top = 0, left = 0;

            for (int row = 0; row < squaresToCreate; row++)
            {
                for (int column = 0; column < squaresToCreate; column++)
                {
                    squareColor = squareColor.Equals(whiteColor) ? blackColor : whiteColor;

                    ChessPiece? currentPiece = _currentGame[row, column];

                    pictureSquares[row, column] = new Label()
                    {
                        Name = $"{row}{column}",
                        BackColor = squareColor,
                        Size = new Size(squareLength, squareLength),
                        Location = new Point(left, top),
                        Text = currentPiece != null ? currentPiece.ReturnPieceTypeName() : String.Empty,
                        TextAlign = ContentAlignment.MiddleCenter,
                        ForeColor = squareColor == blackColor ? Color.White : Color.Black
                    };

                    pictureSquares[row, column].Click += new EventHandler(this.SquareClickedEvent!);

                    MainBoard.Controls.Add(pictureSquares[row, column]);

                    left += squareLength;
                }
                left = 0;
                top += squareLength;
                // Alternate here so that the first square in the new row will be different than the one in the previous row.
                squareColor = squareColor.Equals(whiteColor) ? blackColor : whiteColor;
            }
        }

        private void SquareClickedEvent(object sender, EventArgs e)
        {
            // First determine if a piece friendly to the player has been selected.
            // If not do nothing, Else display available moves.

            if (this._resetSquareAssignments)
            {
                this._targetSquare = null;
                this._friendlySelectedSquare = null;
                this._resetSquareAssignments = false;
            }

            var selectedSquare = sender as Label;

            string availableMovesHex = "#B7AEC5";
            string selectedSquareHex = "#D6B8FF";
            // Label Name is a string containging the row and colrumn coordinates: {row}{column}
            int[] coords = (from cc in selectedSquare!.Name.ToCharArray()
                            select Int32.Parse(cc.ToString())).ToArray();

            ChessPiece? selectedPiece = _currentGame[coords[0], coords[1]];

            if (selectedPiece != null && selectedPiece.PieceTeam == _activePlayer.CurrentTeam && !selectedSquare.Equals(_friendlySelectedSquare))
            {   // Display available movements for the ChessPiece

                if (_targetSquare != null)
                {
                    // _set targetSquare to null so that it isn't changed unnecessarily by the if statemnt that changes squares back to the avalailbleMovesHex..
                    this.TemporaryTextReversal();
                    _targetSquare = null;
                }

                _friendlySelectedSquare = selectedSquare;

                _movesAvailableToPiece = selectedPiece.AvailableMoves(_currentGame.GameBoard, false, false);

                // List of squares that can be moved to. 
                _validSquares = (from move in _movesAvailableToPiece
                                 let labelName = $"{(int)move.MainNewLocation.X}{(int)move.MainNewLocation.Y}"
                                 select MainBoard.Controls[labelName] as Label).ToList();

                this.ResetSquareColors();


                // Create a list of structs that will be used to revert any changes in borderstyle or backColor.
                _changedSquares = (from square in _validSquares
                                   select new OriginalBackColor(square)).ToList();

                _changedSquares.Add(new OriginalBackColor(_friendlySelectedSquare));

                _friendlySelectedSquare.BackColor = ColorTranslator.FromHtml(selectedSquareHex);
                _friendlySelectedSquare.BorderStyle = BorderStyle.FixedSingle;

                // Highlight potential moves for the selected piece.
                foreach (var square in _validSquares)
                {
                    square.BackColor = ColorTranslator.FromHtml(availableMovesHex);
                    square.BorderStyle = BorderStyle.FixedSingle;
                }

                ConfirmMove.Visible = false;

                _friendlyText = _friendlySelectedSquare.Text;

            }
            else if (_validSquares.Contains(selectedSquare))
            {
                // This statement executes if the user chooses a [DIFFERENT] square within the list of available moves.
                if (_targetSquare != null)
                {
                    _targetSquare.BackColor = ColorTranslator.FromHtml(availableMovesHex);

                    // Undo changes.
                    if (_friendlySelectedSquare != null && _targetSquare != null)
                    {
                        _targetSquare.Text = _targetText;
                    }
                }

                selectedSquare.BackColor = ColorTranslator.FromHtml(selectedSquareHex);

                _targetSquare = selectedSquare;

                _friendlySelectedSquare!.Text = string.Empty;

                _targetText = _targetSquare.Text;
                _targetSquare.Text = _friendlyText;

                ConfirmMove.Visible = true;
                // Change label visuals.
            }
            else if (!selectedSquare.Equals(_friendlySelectedSquare))
            {

                this.TemporaryTextReversal();

                this.ResetSquareColors();
                _resetSquareAssignments = true;
                _validSquares.Clear();
                ConfirmMove.Visible = false;
            }
        }
        public void ConfirmMoveClickedEvent(object sender, EventArgs e)
        {
            ConfirmMove.Visible = false;

            _resetSquareAssignments = true;
            _validSquares.Clear();
            this.ResetSquareColors();

            // Get coordinates from selected square. Move has been validated if this event is available.
            int[] coords = (from cc in _targetSquare!.Name.ToCharArray()
                            select Int32.Parse(cc.ToString())).ToArray();

            MovementInformation submitedMovement = (from cm in this._movesAvailableToPiece
                                                    let newSquareVector = new Vector2(coords[0], coords[1])
                                                    where Vector2.Equals(cm.MainNewLocation, newSquareVector)
                                                    select cm).First();

            if (submitedMovement.SecondaryPiece != null)
            {
                int secondPieceRow = (int)submitedMovement.SecondaryPiece.ReturnLocation(0);
                int secondPieceColumn = (int)submitedMovement.SecondaryPiece.ReturnLocation(1);

                if (submitedMovement.CastlingWithSecondary)
                {
                    int newColumn = (int)submitedMovement.SecondaryNewLocation.Y;

                    pictureSquares[secondPieceRow, newColumn].Text = pictureSquares[secondPieceRow, secondPieceColumn].Text;

                    pictureSquares[secondPieceRow, secondPieceColumn].Text = string.Empty;

                }
                else if (submitedMovement.CapturingSecondary && !Vector2.Equals(submitedMovement.MainNewLocation, submitedMovement.SecondaryPiece.currentLocation))
                {
                    // En Passant Capture.
                    pictureSquares[secondPieceRow, secondPieceColumn].Text = string.Empty;
                }
            }

            // Send change to the GameEnviroment instance.
            this._currentGame.SubmitChange(submitedMovement);

            /// Change the _activePlayer variable and remove its vunerability to En Passant.
            Team newActivePlayerTeamColor;

            if (_activePlayer == _currentGame.WhitePlayer)
            {
                this._activePlayer = _currentGame.BlackPlayer;
                newActivePlayerTeamColor = Team.Black;
            }
            else
            {
                this._activePlayer = _currentGame.WhitePlayer;
                newActivePlayerTeamColor = Team.White;
            }
            // Disable the newly active players vulnerability to En Passant.
            _currentGame.DisablePlayerVulnerabilityToEnPassant(newActivePlayerTeamColor);

        }

        /// <summary>
        /// Changes a label found in _changedSquares back to their original backColor and borderStyle.
        /// </summary>
        private void ResetSquareColors()
        {
            foreach (var squareInfo in _changedSquares)
            {
                squareInfo.SquareChanged.BackColor = squareInfo.OriginalColor;
                squareInfo.SquareChanged.BorderStyle = BorderStyle.None;
            }
        }
        private void TemporaryTextReversal()
        {
            if (_friendlySelectedSquare != null) _friendlySelectedSquare.Text = _friendlyText;
            if (_targetSquare != null) _targetSquare.Text = _targetText;
        }
    }
}