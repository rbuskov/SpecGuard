using SpecGuard.TicTacToeApi.Models;

namespace SpecGuard.TicTacToeApi.Services;

public sealed class BoardState
{
    private static readonly (int Row, int Col)[][] WinningLines =
    [
        [(0, 0), (0, 1), (0, 2)],
        [(1, 0), (1, 1), (1, 2)],
        [(2, 0), (2, 1), (2, 2)],
        [(0, 0), (1, 0), (2, 0)],
        [(0, 1), (1, 1), (2, 1)],
        [(0, 2), (1, 2), (2, 2)],
        [(0, 0), (1, 1), (2, 2)],
        [(0, 2), (1, 1), (2, 0)],
    ];

    private readonly Lock gate = new();
    private readonly Mark[][] squares =
    [
        [Mark.Empty, Mark.Empty, Mark.Empty],
        [Mark.Empty, Mark.Empty, Mark.Empty],
        [Mark.Empty, Mark.Empty, Mark.Empty],
    ];

    public Mark[][] Snapshot()
    {
        lock (gate)
        {
            return
            [
                [squares[0][0], squares[0][1], squares[0][2]],
                [squares[1][0], squares[1][1], squares[1][2]],
                [squares[2][0], squares[2][1], squares[2][2]],
            ];
        }
    }

    public Mark Get(int row, int column)
    {
        lock (gate)
        {
            return squares[row - 1][column - 1];
        }
    }

    public SetOutcome TrySet(int row, int column, Mark mark)
    {
        lock (gate)
        {
            if (squares[row - 1][column - 1] != Mark.Empty)
            {
                return SetOutcome.NotEmpty;
            }

            squares[row - 1][column - 1] = mark;
            return SetOutcome.Success;
        }
    }

    public Mark Winner()
    {
        lock (gate)
        {
            foreach (var line in WinningLines)
            {
                var value = squares[line[0].Row][line[0].Col];
                if (value != Mark.Empty &&
                    value == squares[line[1].Row][line[1].Col] &&
                    value == squares[line[2].Row][line[2].Col])
                {
                    return value;
                }
            }

            return Mark.Empty;
        }
    }

    public enum SetOutcome
    {
        Success,
        NotEmpty,
    }
}
