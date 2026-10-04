namespace Requests.Application.Requests.Entities;

// Stored as tinyint. Values are explicit so reordering the enum never changes stored data.
public enum RequestStatus : byte
{
    New = 0,
    InProgress = 1,
    Waiting = 2,
    Completed = 3
}
