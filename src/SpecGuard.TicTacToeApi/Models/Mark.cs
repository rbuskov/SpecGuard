using System.Text.Json.Serialization;

namespace SpecGuard.TicTacToeApi.Models;

public enum Mark
{
    [JsonStringEnumMemberName(".")] Empty,
    X,
    O,
}
