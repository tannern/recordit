
namespace Recordit.Models;

public class SessionDatabase
{
    public int NextSessionId { get; set; } = 1;

    public List<Session> Sessions { get; set; } = [];
}
