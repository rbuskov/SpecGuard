using Microsoft.AspNetCore.Mvc;
using SpecGuard.TicTacToeApi.Models;
using SpecGuard.TicTacToeApi.Services;

namespace SpecGuard.TicTacToeApi.Endpoints;

public static class BoardEndpoints
{
    public static IEndpointRouteBuilder MapBoardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var board = new BoardState();

        endpoints.MapGet("/board", () => new BoardStatus(board.Winner(), board.Snapshot()))
            .WithName("get-board")
            .Produces<BoardStatus>(StatusCodes.Status200OK);

        endpoints.MapGet("/board/{row:int}/{column:int}",
                (int row, int column) => board.Get(row, column))
            .WithName("get-square")
            .Produces<Mark>(StatusCodes.Status200OK);

        endpoints.MapPut("/board/{row:int}/{column:int}",
                (int row, int column, [FromBody] Mark mark) =>
                {
                    var outcome = board.TrySet(row, column, mark);
                    if (outcome == BoardState.SetOutcome.NotEmpty)
                    {
                        return Results.Text("Square is not empty.", "text/html", statusCode: 400);
                    }

                    return Results.Ok(new BoardStatus(board.Winner(), board.Snapshot()));
                })
            .WithName("put-square")
            .Accepts<Mark>("application/json")
            .Produces<BoardStatus>(StatusCodes.Status200OK);

        return endpoints;
    }
}
