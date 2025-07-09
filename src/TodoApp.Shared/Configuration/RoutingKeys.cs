namespace TodoApp.Shared.Configuration;

public static class RoutingKeys
{
    public const string UserEvents = "user.*";
    public const string TodoEvents = "todo.*";
    
    public static class Specific
    {
        public const string UserCreated = "user.created";
        public const string UserUpdated = "user.updated";
        public const string UserDeleted = "user.deleted";
        
        public const string TodoCreated = "todo.created";
        public const string TodoUpdated = "todo.updated";
        public const string TodoDeleted = "todo.deleted";
    }
}
