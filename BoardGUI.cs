
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
        /// <value>Button that appears when a valid move has been selected.</value>
        private Button ConfirmMove;

        /// <value>Container for all <c>PictureBox</c> objects contained within <paramref name="pictureSquares"/>.</value>
        private Panel MainBoard;

        /// <value>2D array of <c>PictureBox</c> objects that represent the visible board.</value>
        private PictureBox[,] pictureSquares;

        /// <value>A <c>GameEnvironment</c> instance that is mapped to the GUI.</value>
        private readonly GameEnvironment _currentGame;

        /// <value>List of squares that a piece friendly to <paramref name="User"/> can move to.</value>
        private List<PictureBox> _validSquares = new();

        /// <value>Valid square that a chess piece can move to.</value>
        private PictureBox? _targetSquare = null;

        /// <value>A <c>PictureBox</c> that represents a square containing a piece friendly to <paramref name="User"/>.</value>
        private PictureBox? _friendlySelectedSquare = null;

        /// <value>Player whose turn it currently is.</value>
        private readonly Player User;

        /// <value>List of squares within <paramref name="pictureSquares"/> that have undergone a temporary visual change.</value>
        private List<OriginalBackColor> _changedSquares = new();

        /// <value>List of available moves to the currently selected piece friendly to the <paramref name="User"/>.</value>
        private List<MovementInformation>? _movesAvailableToPiece;

        /// <value>Stores a temporary reference to an image when selecting an available movement.</value>
        private Image? _targetImage;

        /// <value>Stores a temporary reference to the friendly piece selected by <paramref name="User"/></value>
        private Image? _friendlyImage;

        /// <value>If set to true then references to temp images and previously selected squares will be set to null.</value>
        private bool _resetSquareAssignments;

        public BoardGUI(Player user, GameEnvironment newGame)
        {
            _currentGame = newGame;
            User = user;
            InitializeComponent();
        }

        [MemberNotNull(nameof(MainBoard), nameof(ConfirmMove), nameof(pictureSquares))]
        private void InitializeComponent()
        {
            MainBoard = new Panel()
            {
                Location = new Point(16, 15),
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
        [MemberNotNull(nameof(pictureSquares))]
        private void CreateBoard()
        {
            int squaresToCreate = _currentGame.GameBoard.GetUpperBound(0) + 1;

            pictureSquares = new PictureBox[squaresToCreate, squaresToCreate];

            int squareLength = MainBoard.Width / squaresToCreate;

            Color blackColor = ColorTranslator.FromHtml("#300d21");
            Color whiteColor = ColorTranslator.FromHtml("#C79F67");

            int top = 0, heightIncrementer = squareLength;

            if (_currentGame.PlayerTeam == Team.White)
            {
                top = MainBoard.Height - squareLength;
            }
            else if (_currentGame.PlayerTeam == Team.Black)
            {
                top = 0;
                heightIncrementer *= -1;
            }

            int left = 0;

            string imageFolder = Path.GetFullPath(@"..\..\..\Resources\");

            for (int row = 0; row < squaresToCreate; row++)
            {
                for (int column = 0; column < squaresToCreate; column++)
                {
                    ChessPiece? currentPiece = _currentGame[row, column];

                    pictureSquares[row, column] = new PictureBox()
                    {
                        Name = $"{row}{column}",
                        BackColor = (row + column) % 2 == 0 ? blackColor : whiteColor,
                        Size = new Size(squareLength, squareLength),
                        Location = new Point(left, top),
                        Image = currentPiece == null ? null : Image.FromFile(imageFolder + $"{currentPiece.ReturnPieceTypeName()}.jpg"),
                        SizeMode = PictureBoxSizeMode.CenterImage
                    };

                    pictureSquares[row, column].Click += new EventHandler(this.SquareClickedEvent!);

                    MainBoard.Controls.Add(pictureSquares[row, column]);

                    left += squareLength;
                }
                left = 0;
                top += heightIncrementer;
            }
            User.Squares = pictureSquares;
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
                _targetSquare = null;
                _friendlySelectedSquare = null;
                _resetSquareAssignments = false;
            }

            if (_currentGame.CanBeInteractedWith == false) return;

            var selectedSquare = sender as PictureBox;

            Color availableMovesColorLight = ColorTranslator.FromHtml("#FF4935");
            Color availableMovesColorDark = ColorTranslator.FromHtml("#7A130F");

            Color moveablePieceColor = ColorTranslator.FromHtml("#58871C");
            Color userSelectedMoveColor = ColorTranslator.FromHtml("#A1C17A");

            // PictureBox.Name is a string containing the row and column coordinates: {row}{column}
            int[] coords = (from cc in selectedSquare!.Name
                            select int.Parse(cc.ToString())).ToArray();

            ChessPiece? selectedPiece = _currentGame[coords[0], coords[1]];

            if (selectedPiece != null && selectedPiece.PieceTeam == _currentGame.PlayerTeam && !selectedSquare.Equals(_friendlySelectedSquare))
            {   // Display available movements for the ChessPiece

                if (_targetSquare != null)
                {
                    // _targetSquare not being null means that a different friendly piece has been selected
                    // and therefore the previously highlighted available movements need to have their respective colors reverted to normal.
                    RevertPictureDisplay();
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

                // Highlight the selected chess piece.
                _friendlySelectedSquare.BackColor = moveablePieceColor;

                // Highlight potential moves for the selected piece.
                foreach (var square in _validSquares)
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
            else if (_validSquares.Contains(selectedSquare))
            {
                // This statement executes if the user chooses a [DIFFERENT] square within the list of available moves.
                if (_targetSquare != null)
                {
                    int coordSum = (from cc in _targetSquare.Name
                                    select int.Parse(cc.ToString())).Sum();
                    // Revert a previously selected squares backColor property.
                    _targetSquare.BackColor = (coordSum % 2) == 0 ? availableMovesColorDark : availableMovesColorLight;

                    // Undo changes.
                    if (_friendlySelectedSquare != null)
                    {
                        _targetSquare.Image = _targetImage;
                    }
                }

                _targetSquare = selectedSquare;
                _targetSquare.BackColor = userSelectedMoveColor;
                _targetImage = _targetSquare.Image;
                _targetSquare.Image = _friendlyImage;

                _friendlySelectedSquare!.Image = null;
                ConfirmMove.Visible = true;

            }
            else if (!selectedSquare.Equals(_friendlySelectedSquare))
            {
                // An invalid square has been selected.
                RevertPictureDisplay();
                ResetSquareColors();
                _resetSquareAssignments = true;
                _validSquares.Clear();
                ConfirmMove.Visible = false;
            }
        }

        /// <summary>
        /// This event submits a movement to the <c>GameEnvironment</c> instance.
        /// </summary>
        /// <param name="sender"><c>PictureBox</c> object that has been clicked.</param>
        public void ConfirmMoveClickedEvent(object sender, EventArgs e)
        {
            ConfirmMove.Visible = false;

            _resetSquareAssignments = true;
            _validSquares.Clear();
            ResetSquareColors();

            // Get coordinates from selected square. Move has been validated if this event is available.
            int[] coords = (from cc in _targetSquare!.Name
                            select int.Parse(cc.ToString())).ToArray();

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
                else if (submitedMovement.CapturingSecondary && !Equals(submitedMovement.MainNewLocation, secondPiece.CurrentLocation))
                {   // En Passant Capture conditions met.
                    pictureSquares[secondPieceRow, secondPieceColumn].Image = null;
                }
            }

            // Send change to the GameEnviroment instance and switch to the next player.
            _currentGame.SubmitFinalizedChange(submitedMovement);
            User.SubmitMoveToServer(submitedMovement, _currentGame.GameID);
        }

        /// <summary>
        /// Changes a <c>PictureBox</c>c found in <paramref name="_changedSquares"/> back to their original backColor and borderStyle.
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
        /// Reverts <paramref name="_friendlySelectedSquare"/> and <paramref name="_targetSquare"/> to their pre-click Image values.
        /// </summary>
        private void RevertPictureDisplay()
        {
            if (_friendlySelectedSquare != null) _friendlySelectedSquare.Image = _friendlyImage;
            if (_targetSquare != null) _targetSquare.Image = _targetImage;
        }
    }
}
