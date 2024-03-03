
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Pieces;

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
        public Color OriginalColor { get; init; }
    }
    public class BoardGUI : UserControl
    {
        /// <summary>Button that appears when a valid move has been selected.</summary>
        private Button ConfirmMove;

        /// <summary>Container for all <see cref="PictureBox"/> objects contained within <see cref="_pictureSquares"/>.</summary>
        private Panel MainBoard;

        /// <summary>2D array of <see cref="PictureBox"/> objects that represent the visible board.</summary>
        private PictureBox[,] _pictureSquares;

        /// <summary>A <see cref="GameEnvironment"/> instance that is mapped to the GUI.</summary>
        private readonly GameEnvironment _currentGame;

        /// <summary>List of squares that a piece friendly to <see cref="User"/> can move to.</summary>
        private List<PictureBox> _validMovementSquares = new();

        /// <summary>Valid square that a chess piece can move to.</summary>
        private PictureBox? _chessPieceDestinationSquare = null;

        /// <summary>A <see cref="PictureBox"/> that represents a square containing a piece friendly to <see cref="User"/>.</summary>
        private PictureBox? _friendlySelectedSquare = null;

        /// <summary><see cref="Playerr"/> instance used to communicate with the server.</summary>
        private readonly Player User;

        /// <summary>List of squares within <see cref="_pictureSquares"/> that have undergone a temporary visual change.</summary>
        private List<OriginalBackColor> _changedSquares = new();

        /// <summary>List of available moves to the currently selected piece friendly to the <see cref="User"/>.</summary>
        private List<MovementInformation>? _movesAvailableToPiece;

        /// <summary>Stores a temporary reference to an image when selecting an available movement.</summary>
        private Image? _targetImage;

        /// <summary>Stores a temporary reference to the friendly piece selected by <see cref="User"/></summary>
        private Image? _friendlyImage;

        /// <summary>If <see langword="true"/> then <see cref="_friendlySelectedSquare"/> and <see cref="_chessPieceDestinationSquare"/> will be set to <see langword="null"/> when <see cref="BoardGUI.SquareClickedEvent(object, EventArgs)"/> is called.</summary>
        private bool _resetSquareAssignments;
        /// <summary>Static vaariablie used to track the <see cref="BoardGUI"/> instance count.</summary>
        private static int s_instanceCount = 0;

        public int CurrentInstanceCount { get; init; }

        public BoardGUI(Player user, GameEnvironment newGame)
        {
            CurrentInstanceCount = ++s_instanceCount;
            Name = CurrentInstanceCount.ToString();
            _currentGame = newGame;
            User = user;
            GenerateBoardElements();
        }

        [MemberNotNull(nameof(MainBoard), nameof(ConfirmMove), nameof(_pictureSquares))]
        private void GenerateBoardElements()
        {
            MainBoard = new Panel()
            {
                Location = new Point(0, 0),
                Name = "MainBoard",
                Size = new Size(640, 640),
                TabIndex = 0
            };

            ConfirmMove = new Button()
            {
                BackColor = Color.FromArgb(153, 218, 56),
                Location = new Point(710, 126),
                Name = "ConfirmMove",
                Size = new Size(322, 75),
                TabIndex = 1,
                Text = "Confirm Selection",
                UseVisualStyleBackColor = false,
                Visible = false
            };

            CreateBoard();

            ConfirmMove.Click += new EventHandler(this.ConfirmMoveClickedEvent!);
            Controls.AddRange(new Control[] { MainBoard, ConfirmMove });
        }

        /// <summary>
        /// Creates the visual representation of game board squares.
        /// </summary>
        [MemberNotNull(nameof(_pictureSquares))]
        private void CreateBoard()
        {
            int squaresToCreate = _currentGame.GameBoard.GetUpperBound(0) + 1;

            _pictureSquares = new PictureBox[squaresToCreate, squaresToCreate];

            int squareLength = MainBoard.Width / squaresToCreate;

            Color blackColor = ColorTranslator.FromHtml("#300d21");
            Color whiteColor = ColorTranslator.FromHtml("#C79F67");

            int top = 0, heightIncrementer = squareLength, left = 0, rightMostSquareLeftValue = MainBoard.Width - squareLength, xIncrementer = squareLength;

            if (_currentGame.PlayerTeam == Team.White)
            {   // Black will be palced at the top of the board.
                top = MainBoard.Height - squareLength;
                heightIncrementer *= -1;
            }
            else if (_currentGame.PlayerTeam == Team.Black)
            {   // White will be placed at the top of the board.
                left = rightMostSquareLeftValue;
                xIncrementer *= -1;
            }

            //C: \Users\Yliyah\Desktop\Resources\White_Rook.jpg
            string imageFolder = Path.GetFullPath(@"..\..\..\Resources\");
            if (!Directory.Exists(imageFolder))
            {
                imageFolder = Path.GetFullPath(@"Resources\");
            }

            for (int row = 0; row < squaresToCreate; row++)
            {
                for (int column = 0; column < squaresToCreate; column++)
                {
                    ChessPiece? currentPiece = _currentGame[row, column];

                    _pictureSquares[row, column] = new PictureBox()
                    {
                        Name = $"{row}{column}",
                        BackColor = (row + column) % 2 == 0 ? blackColor : whiteColor,
                        Size = new Size(squareLength, squareLength),
                        Location = new Point(left, top),
                        Image = currentPiece == null ? null : Image.FromFile(imageFolder + $"{currentPiece.ReturnPieceTypeName()}.jpg"),
                        SizeMode = PictureBoxSizeMode.CenterImage
                    };

                    _pictureSquares[row, column].Click += new EventHandler(this.SquareClickedEvent!);
                    MainBoard.Controls.Add(_pictureSquares[row, column]);
                    left += xIncrementer;
                }
                left = _currentGame.PlayerTeam == Team.White ? 0 : rightMostSquareLeftValue;
                top += heightIncrementer;
            }
            _currentGame.Squares = _pictureSquares;
        }
        /// <summary>
        /// This event handler is used to handle click events on squares within the current board.
        /// </summary>
        private void SquareClickedEvent(object sender, EventArgs e)
        {
            // First determine if a piece friendly to the player has been selected.
            // If not do nothing, Else display available moves.
            if (_resetSquareAssignments)
            {
                _chessPieceDestinationSquare = null;
                _friendlySelectedSquare = null;
                _resetSquareAssignments = false;
            }
            // Check if the user is allowed to interact with the game board.
            if (_currentGame.ActiveTeam != _currentGame.PlayerTeam || _currentGame.GameEnded) return;

            var selectedSquare = sender as PictureBox;

            Color availableMovesColorLight = ColorTranslator.FromHtml("#FF4935");
            Color availableMovesColorDark = ColorTranslator.FromHtml("#7A130F");

            Color moveablePieceColor = ColorTranslator.FromHtml("#58871C");
            Color userSelectedMoveColor = ColorTranslator.FromHtml("#A1C17A");

            // PictureBox.Name is a string containing the row and column coordinates: {row}{column}.
            int[] coords = (from cc in selectedSquare!.Name
                            select int.Parse(cc.ToString())).ToArray();

            ChessPiece? selectedPiece = _currentGame[coords[0], coords[1]];

            if (selectedPiece?.AssignedTeam == _currentGame.PlayerTeam && !selectedSquare.Equals(_friendlySelectedSquare))
            {   // Display available movements for the ChessPiece
                if (_chessPieceDestinationSquare is not null)
                {
                    // _chessPieceDestinationSquare not being null means that a different friendly piece has been selected
                    // and therefore the previously highlighted available movements need to have their respective colors reverted to normal.
                    RevertPictureDisplay();
                    _chessPieceDestinationSquare = null;
                }

                _friendlySelectedSquare = selectedSquare;
                _movesAvailableToPiece = _currentGame.AvailableMoves(selectedPiece);

                ResetSquareColors();
                // List of squares that can be moved to. 
                _validMovementSquares = (from move in _movesAvailableToPiece
                                         let labelName = $"{move.NewMainCoords.RowIndex}{move.NewMainCoords.ColumnIndex}"
                                         select MainBoard.Controls[labelName] as PictureBox).ToList();

                // Create a list of structs that will be used to revert any changes in borderstyle or backColor.
                _changedSquares = (from square in _validMovementSquares
                                   select new OriginalBackColor(square)).ToList();

                _changedSquares.Add(new OriginalBackColor(_friendlySelectedSquare));
                // Highlight the selected chess piece.
                _friendlySelectedSquare.BackColor = moveablePieceColor;

                // Highlight potential moves for the selected piece.
                foreach (var square in _validMovementSquares)
                {
                    int coordSum = (from cc in square.Name
                                    select int.Parse(cc.ToString())).Sum();
                    Color availableMovesColor = (coordSum % 2) == 0 ? availableMovesColorDark : availableMovesColorLight;
                    square.BackColor = availableMovesColor;
                }

                ConfirmMove.Visible = false;
                // Store a reference to the current image so that it can be placed
                // onto other squares or have any changes to _friendlySelectedSquare reversed.
                _friendlyImage = _friendlySelectedSquare.Image;
            }
            else if (_validMovementSquares.Contains(selectedSquare))
            {
                // This statement executes if the user chooses a [DIFFERENT] square within the list of available moves.
                if (_chessPieceDestinationSquare != null)
                {
                    // Calculating the sum of the coordinates will allow you to calculate what color the square should be.
                    int coordSum = (from cc in _chessPieceDestinationSquare.Name
                                    select int.Parse(cc.ToString())).Sum();
                    // Revert a previously selected squares backColor property.
                    _chessPieceDestinationSquare.BackColor = (coordSum % 2) == 0 ? availableMovesColorDark : availableMovesColorLight;
                    // Undo previoulsy made changes.
                    if (_friendlySelectedSquare != null) _chessPieceDestinationSquare.Image = _targetImage;
                }

                _chessPieceDestinationSquare = selectedSquare;
                _chessPieceDestinationSquare.BackColor = userSelectedMoveColor;
                _targetImage = _chessPieceDestinationSquare.Image;
                _chessPieceDestinationSquare.Image = _friendlyImage;

                _friendlySelectedSquare!.Image = null;
                ConfirmMove.Visible = true;
            }
            else if (!selectedSquare.Equals(_friendlySelectedSquare))
            {
                // An invalid square has been selected.
                RevertPictureDisplay();
                ResetSquareColors();
                _resetSquareAssignments = true;
                _validMovementSquares.Clear();
                ConfirmMove.Visible = false;
            }
        }

        /// <summary>
        /// This event submits a movement to the <see cref="GameEnvironment"/> instance.
        /// </summary>
        /// <param name="sender"><see cref="PictureBox"/> object that has been clicked.</param>
        public async void ConfirmMoveClickedEvent(object sender, EventArgs e)
        {
            // Ensure the user can no longer submit a move.
            ConfirmMove.Visible = false;

            _resetSquareAssignments = true;
            _validMovementSquares.Clear();
            ResetSquareColors();

            // Get coordinates from selected square. Move has been validated if this event is available.
            var row = int.Parse(_chessPieceDestinationSquare!.Name[0].ToString());
            var column = int.Parse(_chessPieceDestinationSquare.Name[1].ToString());

            MovementInformation submittedMovement = (from cm in _movesAvailableToPiece
                                                     let newSquareVector = new Vector2(column, row)
                                                     where Equals(cm.MainNewLocation, newSquareVector)
                                                     select cm).First();
            // The main friendly piece has already been moved so move secondary pieces that haven't been replaced.
            if (submittedMovement.SecondaryCopy is not null)
            {
                ChessPiece piece = submittedMovement.SecondaryCopy;

                if (submittedMovement.CastlingWithSecondary)
                {
                    _pictureSquares[(int)submittedMovement.NewSecondaryCoords?.RowIndex!, (int)submittedMovement.NewSecondaryCoords?.ColumnIndex!].Image = _pictureSquares[piece.CurrentRow, piece.CurrentColumn].Image;
                    _pictureSquares[piece.CurrentRow, piece.CurrentColumn].Image = null;
                }
                else if (submittedMovement.CapturingViaEnPassant)
                {   // If the current location is not the same as the final location of MainPiece then the movemtn is an En Passant capture.
                    _pictureSquares[piece.CurrentRow, piece.CurrentColumn].Image = null;
                }
            }
            // If a pawn reaches the opposite side of the board, prompt the user to select a different piece type.
            if (submittedMovement.MainCopy.AssignedType == PieceType.Pawn && new int[] { 0, 7 }.Contains(submittedMovement.NewMainCoords.RowIndex))
            {
                throw new NotImplementedException("Exchanging a pawn for a special piece hasn't been implemented.");
            }
            // Send change to the server and update on local client.
            await User.SubmitMoveToServerAsync(submittedMovement, _currentGame.GameID);
        }

        /// <summary>
        /// Changes a <see cref="PictureBox"/> found in <see cref="_changedSquares"/> back to their original backColor and borderStyle.
        /// </summary>
        private void ResetSquareColors()
        {
            foreach (var squareInfo in _changedSquares)
            {
                squareInfo.SquareChanged.BackColor = squareInfo.OriginalColor;
                squareInfo.SquareChanged.BorderStyle = BorderStyle.None;
            }
        }
        /// <summary>
        /// Reverts <see cref="_friendlySelectedSquare"/> and <see cref="_chessPieceDestinationSquare"/> to their pre-click <see cref="PictureBox.Image"/> values.
        /// </summary>
        private void RevertPictureDisplay()
        {
            if (_friendlySelectedSquare != null) _friendlySelectedSquare.Image = _friendlyImage;
            if (_chessPieceDestinationSquare != null) _chessPieceDestinationSquare.Image = _targetImage;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // BoardGUI
            // 
            this.BackColor = SystemColors.ActiveCaption;
            this.Name = "BoardGUI";
            this.Size = new Size(1040, 685);
            this.ResumeLayout(false);

        }
    }
}
