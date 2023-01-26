using Pieces;
using System.Numerics;

namespace Chess_GUi
{
    public readonly struct OriginalBackColor
    {
        public OriginalBackColor(PictureBox square)
        {
            SquareChanged = square;
            //OriginalImage = square.Image;
            OriginalColor = square.BackColor;
        }
        public PictureBox SquareChanged { get; init; }
        //public Image? OriginalImage { get; init; }
        public Color OriginalColor { get; init; }
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

            pictureSquares = new PictureBox[squaresToCreate, squaresToCreate];

            int squareLength = MainBoard.Width / squaresToCreate;

            Color blackColor = ColorTranslator.FromHtml("#300d21");
            Color whiteColor = Color.MistyRose;

            // Initialize as whiteColor so that the fisrt square made will be blackColor.
            Color squareColor = whiteColor;
            int top = MainBoard.Height - squareLength, left = 0;

            string imageFolder = Path.GetFullPath(@"..\..\..\Resources\");

            for (int row = 0; row < squaresToCreate; row++)
            {
                for (int column = 0; column < squaresToCreate; column++)
                {
                    squareColor = squareColor.Equals(whiteColor) ? blackColor : whiteColor;

                    ChessPiece? currentPiece = _currentGame[row, column];

                    pictureSquares[row, column] = new PictureBox()
                    {
                        Name = $"{row}{column}",
                        BackColor = squareColor,
                        Size = new Size(squareLength, squareLength),
                        Location = new Point(left, top),
                        Text = currentPiece != null ? currentPiece.ReturnPieceTypeName() : String.Empty,
                        Image = currentPiece == null ? null : Image.FromFile(imageFolder+ $"{currentPiece.ReturnPieceTypeName()}.jpg"),
                        SizeMode = PictureBoxSizeMode.CenterImage
                    };

                    pictureSquares[row, column].Click += new EventHandler(this.SquareClickedEvent!);

                    MainBoard.Controls.Add(pictureSquares[row, column]);

                    left += squareLength;
                }
                left = 0;
                top -= squareLength;
                // Alternate here so that the first square in the new row will be different than the one in the previous row.
                squareColor = squareColor.Equals(whiteColor) ? blackColor : whiteColor;
            }
        }

        private void SquareClickedEvent(object sender, EventArgs e)
        {
            // First determine if a piece friendly to the player has been selected.
            // If not do nothing, Else display available moves.

            if (_resetSquareAssignments)
            {
                _targetSquare = null;
                _friendlySelectedSquare = null;
                _resetSquareAssignments = false;
            }

            var selectedSquare = sender as PictureBox;

            string availableMovesHex = "#B7AEC5";
            string selectedSquareHex = "#D6B8FF";
            // PictureBox Name is a string containging the row and colrumn coordinates: {row}{column}
            int[] coords = (from cc in selectedSquare!.Name.ToCharArray()
                            select Int32.Parse(cc.ToString())).ToArray();

            ChessPiece? selectedPiece = _currentGame[coords[0], coords[1]];

            if (selectedPiece != null && selectedPiece.PieceTeam == _activePlayer.CurrentTeam && !selectedSquare.Equals(_friendlySelectedSquare))
            {   // Display available movements for the ChessPiece

                if (_targetSquare != null)
                {
                    // _set targetSquare to null so that it isn't changed unnecessarily by the if statemnt that changes squares back to the avalailbleMovesHex..
                    TemporaryTextReversal();
                    _targetSquare = null;
                }

                _friendlySelectedSquare = selectedSquare;

                _movesAvailableToPiece = selectedPiece.AvailableMoves(_currentGame.GameBoard, false, false);

                ResetSquareColors();
                // List of squares that can be moved to. 
                _validSquares = (from move in _movesAvailableToPiece
                                 let labelName = $"{(int)move.MainNewLocation.X}{(int)move.MainNewLocation.Y}"
                                 select MainBoard.Controls[labelName] as PictureBox).ToList();

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

                _friendlyImage = _friendlySelectedSquare.Image;

            }
            else if (_validSquares.Contains(selectedSquare))
            {
                // This statement executes if the user chooses a [DIFFERENT] square within the list of available moves.
                if (_targetSquare != null)
                {
                    _targetSquare.BackColor = ColorTranslator.FromHtml(availableMovesHex);

                    // Undo changes.
                    if (_friendlySelectedSquare != null)
                    {
                        _targetSquare.Image = _targetImage;
                    }
                }
                _targetSquare = selectedSquare;

                _targetSquare.BackColor = ColorTranslator.FromHtml(selectedSquareHex);

                _friendlySelectedSquare!.Image = null;

                _targetImage = _targetSquare.Image;
                _targetSquare.Image = _friendlyImage;

                ConfirmMove.Visible = true;
                // Change label visuals.
            }
            else if (!selectedSquare.Equals(_friendlySelectedSquare))
            {// An invalid square has been selected.
                TemporaryTextReversal();

                ResetSquareColors();
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
            ResetSquareColors();

            // Get coordinates from selected square. Move has been validated if this event is available.
            int[] coords = (from cc in _targetSquare!.Name.ToCharArray()
                            select Int32.Parse(cc.ToString())).ToArray();

            MovementInformation submitedMovement = (from cm in _movesAvailableToPiece
                                                    let newSquareVector = new Vector2(coords[0], coords[1])
                                                    where Equals(cm.MainNewLocation, newSquareVector)
                                                    select cm).First();

            if (submitedMovement.SecondaryPiece != null)
            {
                ChessPiece secondPiece = submitedMovement.SecondaryPiece;

                int secondPieceRow = secondPiece.ReturnLocation(0);
                int secondPieceColumn = secondPiece.ReturnLocation(1);

                if (submitedMovement.CastlingWithSecondary)
                {
                    int newColumn = (int)submitedMovement.SecondaryNewLocation.Y;

                    pictureSquares[secondPieceRow, newColumn].Image = pictureSquares[secondPieceRow, secondPieceColumn].Image;

                    pictureSquares[secondPieceRow, secondPieceColumn].Image = null;

                }
                else if (submitedMovement.CapturingSecondary && !Equals(submitedMovement.MainNewLocation, secondPiece.currentLocation))
                {
                    // En Passant Capture conditions met.
                    pictureSquares[secondPieceRow, secondPieceColumn].Image = null;
                }
            }

            // Send change to the GameEnviroment instance.
            _currentGame.SubmitChange(submitedMovement);

            /// Change the _activePlayer variable and remove its vunerability to En Passant.
            Team newActivePlayerTeamColor;

            if (_activePlayer == _currentGame.WhitePlayer)
            {
                _activePlayer = _currentGame.BlackPlayer;
                newActivePlayerTeamColor = Team.Black;
            }
            else
            {
                _activePlayer = _currentGame.WhitePlayer;
                newActivePlayerTeamColor = Team.White;
            }
            // Disables the newly active players vulnerability to En Passant.
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
                //squareInfo.SquareChanged.Image = 
            }
        }
        /// <summary>
        /// Reverts _friendlySelectedSquare and _targetSquare text to their pre-clck values.
        /// </summary>
        private void TemporaryTextReversal()
        {
            if (_friendlySelectedSquare != null) _friendlySelectedSquare.Image = _friendlyImage;
            if (_targetSquare != null) _targetSquare.Image = _targetImage;
        }
    }
}