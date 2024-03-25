using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
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
        public Button ConfirmMoveBTN { get; private set; }

        /// <summary>Container for all <see cref="PictureBox"/> objects contained within <see cref="_pictureSquares"/>.</summary>
        private Panel _mainBoard;

        /// <summary>2D array of <see cref="PictureBox"/> objects that represent the visible board.</summary>
        private PictureBox[,] _pictureSquares;

        /// <summary>A <see cref="GameEnvironment"/> instance that is mapped to the GUI.</summary>
        private readonly GameEnvironment _currentGame;

        /// <summary>List of squares that a piece friendly to <see cref="_player"/> can move to.</summary>
        private List<PictureBox> _validMovementSquares = new();

        /// <summary>Valid square that a chess piece can move to.</summary>
        private PictureBox? _chessPieceDestinationSquare = null;

        /// <summary>A <see cref="PictureBox"/> that represents a square containing a piece friendly to <see cref="_player"/>.</summary>
        private PictureBox? _previousFriendlySquare = null;

        /// <summary><see cref="Playerr"/> instance used to communicate with the server.</summary>
        private readonly Player _player;

        /// <summary>List of squares within <see cref="_pictureSquares"/> that have undergone a temporary visual change.</summary>
        private List<OriginalBackColor> _changedSquares = new();

        /// <summary>List of available moves to the currently selected piece friendly to the <see cref="_player"/>.</summary>
        private List<MovementInformation>? _movesAvailableToPiece;

        /// <summary>Stores a temporary reference to an image when selecting an available movement.</summary>
        private Image? _targetImage;

        /// <summary>Stores a temporary reference to the friendly piece selected by <see cref="_player"/></summary>
        private Image? _friendlyImage;

        /// <summary>If <see langword="true"/> then <see cref="_previousFriendlySquare"/> and <see cref="_chessPieceDestinationSquare"/> will be set to <see langword="null"/> when <see cref="BoardGUI.SquareClickedEvent(object, EventArgs)"/> is called.</summary>
        private bool _resetSquareAssignments;
        public bool InteractionsDisabled { get; set; } = false;
        public GameState StateOfGame { get => _currentGame.MatchState; }

        public static string PathToResources { get; } = Path.Combine(Assembly.GetExecutingAssembly().Location, "..\\..\\..\\..\\Resources\\");

        private MovementInformation? _moveToSubmit = null;
        public BoardGUI(Player user, GameEnvironment newGame, string nameOfGui)
        {
            Name = nameOfGui;
            _currentGame = newGame;
            _player = user;
            GenerateBoardElements();
        }

        [MemberNotNull(nameof(_mainBoard), nameof(ConfirmMoveBTN), nameof(_pictureSquares))]
        private void GenerateBoardElements()
        {
            _mainBoard = new Panel()
            {
                Location = new Point(0, 0),
                Name = "_mainBoard",
                Size = new Size(640, 640),
                TabIndex = 0
            };

            ConfirmMoveBTN = new Button()
            {
                BackColor = Color.FromArgb(153, 218, 56),
                Location = new Point(710, 400),
                Name = "ConfirmMoveBTN",
                Size = new Size(200, 75),
                TabIndex = 1,
                Text = "Confirm Selection",
                UseVisualStyleBackColor = false,
                Visible = false
            };

            var _promotionPanel = new Panel()
            {
                BackColor = Color.White,
                Location = new Point(_mainBoard.Right + 50, 300),
                Size = new Size(300, 45),
                Visible = false,
                Name = "PromotionPanel"
            };

            foreach (PieceType chessType in Enum.GetValues(typeof(PieceType)))
            {
                if (chessType != PieceType.King && chessType != PieceType.Pawn)
                {
                    var width = _promotionPanel.Width / 4;
                    var btn = new Button()
                    {
                        Text = chessType.ToString(),
                        Size = new Size(width, _promotionPanel.Height),
                        Name = chessType.ToString(),
                        Location = new Point(_promotionPanel.Controls.Count * width, 0)
                    };
                    btn.Click += new EventHandler(this.PromotePawnSelectionEvent!);
                    _promotionPanel.Controls.Add(btn);
                }
            }

            CreateBoard();

            ConfirmMoveBTN.Click += new EventHandler(this.ConfirmMoveClickedEvent!);
            Controls.AddRange(new Control[] { _mainBoard, ConfirmMoveBTN, _promotionPanel });
        }

        /// <summary>
        /// Creates the visual representation of game board squares.
        /// </summary>
        [MemberNotNull(nameof(_pictureSquares))]
        private void CreateBoard()
        {
            int squaresToCreate = _currentGame.GameBoard.GetUpperBound(0) + 1;

            _pictureSquares = new PictureBox[squaresToCreate, squaresToCreate];

            int squareLength = _mainBoard.Width / squaresToCreate;

            Color blackColor = ColorTranslator.FromHtml("#300d21");
            Color whiteColor = ColorTranslator.FromHtml("#C79F67");

            int top = 0, heightIncrementer = squareLength, left = 0, rightMostSquareLeftValue = _mainBoard.Width - squareLength, xIncrementer = squareLength;

            if (_currentGame.PlayerTeam == Team.White)
            {   // Black will be palced at the top of the board.
                top = _mainBoard.Height - squareLength;
                heightIncrementer *= -1;
            }
            else if (_currentGame.PlayerTeam == Team.Black)
            {   // White will be placed at the top of the board.
                left = rightMostSquareLeftValue;
                xIncrementer *= -1;
            }

            //string imageFolder = Path.Combine(Assembly.GetExecutingAssembly().Location, "..\\..\\..\\Resources\\");

            /*if (!Directory.Exists(imageFolder))
            {
                // USed if dotnet run is used or ran from Visual Studio.
                imageFolder = Path.GetFullPath(@"Resources\");
            }*/

            for (int row = 0; row < squaresToCreate; row++)
            {
                for (int column = 0; column < squaresToCreate; column++)
                {
                    ChessPiece? currentPiece = _currentGame.GameBoard[row, column];

                    _pictureSquares[row, column] = new PictureBox()
                    {
                        Name = $"{row}{column}",
                        BackColor = (row + column) % 2 == 0 ? blackColor : whiteColor,
                        Size = new Size(squareLength, squareLength),
                        Location = new Point(left, top),
                        Image = currentPiece == null ? null : Image.FromFile(PathToResources + $"{currentPiece.ReturnPieceTypeName()}.jpg"),
                        SizeMode = PictureBoxSizeMode.CenterImage
                    };

                    _pictureSquares[row, column].Click += new EventHandler(this.SquareClickedEvent!);
                    _mainBoard.Controls.Add(_pictureSquares[row, column]);
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
            Controls["PromotionPanel"].Visible = false;
            if (InteractionsDisabled) return;
            if (_resetSquareAssignments)
            {
                _chessPieceDestinationSquare = null;
                _previousFriendlySquare = null;
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

            ChessPiece? selectedPiece = _currentGame.GameBoard[coords[0], coords[1]];

            // First determine if a piece friendly to the player has been selected.
            // If true then display available moves, else do nothing.
            if (selectedPiece?.AssignedTeam == _currentGame.PlayerTeam && !selectedSquare.Equals(_previousFriendlySquare))
            {
                // Display available movements for the ChessPiece
                if (_chessPieceDestinationSquare is not null)
                {
                    // _chessPieceDestinationSquare not being null means that a different friendly piece has been selected
                    // and therefore the previously highlighted available movements need to have their respective colors reverted to normal.
                    RevertPictureDisplay();
                    _chessPieceDestinationSquare = null;
                }

                _movesAvailableToPiece = _currentGame.AvailableMoves(selectedPiece);

                ResetSquareColors();
                // List of squares that can be moved to. 
                _validMovementSquares = (from move in _movesAvailableToPiece
                                         let labelName = $"{move.NewMainCoords.RowIndex}{move.NewMainCoords.ColumnIndex}"
                                         select _mainBoard.Controls[labelName] as PictureBox).ToList();

                // Create a list of structs that will be used to revert any changes in borderstyle or backColor.
                _changedSquares = (from square in _validMovementSquares
                                   select new OriginalBackColor(square)).ToList();

                _changedSquares.Add(new OriginalBackColor(selectedSquare));
                // Highlight the selected chess piece.
                selectedSquare.BackColor = moveablePieceColor;

                // Highlight potential moves for the selected piece.
                foreach (var square in _validMovementSquares)
                {
                    int coordSum = (from cc in square.Name
                                    select int.Parse(cc.ToString())).Sum();
                    Color availableMovesColor = (coordSum % 2) == 0 ? availableMovesColorDark : availableMovesColorLight;
                    square.BackColor = availableMovesColor;
                }

                ConfirmMoveBTN.Visible = false;
                // Store a reference to the current image so that it can be placed
                // onto other squares or have any changes to _previousFriendlySquare reversed.
                _friendlyImage = selectedSquare.Image;
                _previousFriendlySquare = selectedSquare;
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
                    if (_previousFriendlySquare != null) _chessPieceDestinationSquare.Image = _targetImage;
                }

                _chessPieceDestinationSquare = selectedSquare;
                _chessPieceDestinationSquare.BackColor = userSelectedMoveColor;
                _targetImage = _chessPieceDestinationSquare.Image;
                _chessPieceDestinationSquare.Image = _friendlyImage;

                _previousFriendlySquare!.Image = null;

                var row = int.Parse(_chessPieceDestinationSquare!.Name[0].ToString());
                var column = int.Parse(_chessPieceDestinationSquare.Name[1].ToString());

                _moveToSubmit = (from cm in _movesAvailableToPiece
                                 let newSquareVector = new Vector2(column, row)
                                 where Equals(cm.MainNewLocation, newSquareVector)
                                 select cm).First();
                if (_moveToSubmit.PromotingPawn)
                {
                    Controls["PromotionPanel"].Visible = true;
                }
                else
                {
                    ConfirmMoveBTN.Visible = true;
                }
            }
            else if (!selectedSquare.Equals(_previousFriendlySquare))
            {
                // An invalid square has been selected.
                RevertPictureDisplay();
                ResetSquareColors();
                _resetSquareAssignments = true;
                _validMovementSquares.Clear();
                ConfirmMoveBTN.Visible = false;
            }
        }

        private void PromotePawnSelectionEvent(object sender, EventArgs e)
        {
            if (Enum.TryParse(((Button)sender).Name, out PieceType selectedPromotion))
            {
                if (new PieceType[] { PieceType.Rook, PieceType.Bishop, PieceType.Knight, PieceType.Queen }.Contains(selectedPromotion))
                {
                    _moveToSubmit!.NewType = selectedPromotion;
                    ConfirmMoveBTN.Visible = true;
                    return;
                }
            }
            throw new ArgumentException("Sender is unknown.");
        }

        /// <summary>
        /// This event submits a movement to the <see cref="GameEnvironment"/> instance.
        /// </summary>
        /// <param name="sender"><see cref="PictureBox"/> object that has been clicked.</param>
        public async void ConfirmMoveClickedEvent(object sender, EventArgs e)
        {
            // Ensure the user can no longer submit a move.
            ConfirmMoveBTN.Visible = false;
            Controls["PromotionPanel"].Visible = false;

            _resetSquareAssignments = true;
            _validMovementSquares.Clear();
            ResetSquareColors();

            // The main friendly piece has already been moved so move secondary pieces that haven't been replaced.
            if (_moveToSubmit!.SecondaryCopy is not null)
            {
                ChessPiece piece = _moveToSubmit.SecondaryCopy;

                if (_moveToSubmit.CastlingWithSecondary)
                {
                    _pictureSquares[(int)_moveToSubmit.NewSecondaryCoords?.RowIndex!, (int)_moveToSubmit.NewSecondaryCoords?.ColumnIndex!].Image = _pictureSquares[piece.CurrentRow, piece.CurrentColumn].Image;
                    _pictureSquares[piece.CurrentRow, piece.CurrentColumn].Image = null;
                }
                else if (_moveToSubmit.CapturingViaEnPassant)
                {   // If the current location is not the same as the final location of MainPiece then the movemtn is an En Passant capture.
                    _pictureSquares[piece.CurrentRow, piece.CurrentColumn].Image = null;
                }
            }

            if (!InteractionsDisabled)
            {
                // If a pawn reaches the opposite side of the board, prompt the user to select a different piece type.
                if (_moveToSubmit.PromotingPawn && _moveToSubmit.MainCopy.AssignedType == PieceType.Pawn && new int[] { 0, 7 }.Contains(_moveToSubmit.NewMainCoords.RowIndex))
                {
                    _pictureSquares[_moveToSubmit.NewMainCoords.RowIndex, _moveToSubmit.NewMainCoords.ColumnIndex].Image = Image.FromFile(PathToResources + $"\\{_moveToSubmit.MainCopy.AssignedTeam}_{_moveToSubmit.NewType}.jpg");
                }

                try
                {
                    await _player.SubmitMoveToServerAsync(_moveToSubmit, _currentGame.GameID);
                }
                catch (IOException)
                {
                    InteractionsDisabled = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    throw;
                }
                _moveToSubmit = null;
            }
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
        /// Reverts <see cref="_previousFriendlySquare"/> and <see cref="_chessPieceDestinationSquare"/> to their pre-click <see cref="PictureBox.Image"/> values.
        /// </summary>
        private void RevertPictureDisplay()
        {
            if (_previousFriendlySquare != null) _previousFriendlySquare.Image = _friendlyImage;
            if (_chessPieceDestinationSquare != null) _chessPieceDestinationSquare.Image = _targetImage;
        }

        public void UpdateBoardBasedOnMove(MovementInformation newMove)
        {
            // Updates Graphics
            // It is important to move the secondary piece first if available.
            if (newMove.SecondaryCopy is not null)
            {
                ChessPiece secPiece = newMove.SecondaryCopy;
                // Interface with the board using coordinates rather than the object.
                PictureBox secBox = _pictureSquares[secPiece.CurrentRow, secPiece.CurrentColumn];

                if (newMove.CapturingSecondary)
                {   // Setting equal to null allows the image to be replaced even if this is an en passant capture.
                    secBox.Image = null;
                }
                else if (newMove.CastlingWithSecondary && newMove.NewSecondaryCoords is not null)
                {
                    _pictureSquares[(int)newMove.NewSecondaryCoords?.RowIndex!, (int)newMove.NewSecondaryCoords?.ColumnIndex!]!.Image = secBox.Image;
                    secBox.Image = null;
                }
            }

            ChessPiece mainPiece = newMove.MainCopy;
            PictureBox mainBox = _pictureSquares[mainPiece.CurrentRow, mainPiece.CurrentColumn];

            if (newMove.PromotingPawn && newMove.MainCopy.AssignedType == PieceType.Pawn && new int[] { 0, 7 }.Contains(newMove.NewMainCoords.RowIndex))
            {
                _pictureSquares[newMove.NewMainCoords.RowIndex, newMove.NewMainCoords.ColumnIndex].Image = Image.FromFile(PathToResources + $"{newMove.MainCopy.AssignedTeam}_{newMove.NewType}.jpg");
            }
            else
            {
                _pictureSquares[newMove.NewMainCoords.RowIndex, newMove.NewMainCoords.ColumnIndex].Image = mainBox.Image;
            }
            mainBox.Image = null;
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
