
namespace Recordit.Models;

public record Session(
    int Id,
    string TaskName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    TimeSpan Duration
);
