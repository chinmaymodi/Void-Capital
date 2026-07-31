namespace VoidCapital.Api.Shared;

/// <summary>Thrown when a resource does not exist. Mapped to HTTP 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>Thrown when a request fails business validation. Mapped to HTTP 400.</summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

/// <summary>Thrown when a trade cannot be funded. Mapped to HTTP 400.</summary>
public class InsufficientFundsException : Exception
{
    public InsufficientFundsException(string message) : base(message) { }
}
