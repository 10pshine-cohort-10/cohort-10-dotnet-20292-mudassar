using System.Text.Json.Serialization;

namespace TaskManager.Api.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskItemStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}


