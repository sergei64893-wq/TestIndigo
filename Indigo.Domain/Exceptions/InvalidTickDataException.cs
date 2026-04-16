namespace Indigo.Domain.Exceptions;

public class InvalidTickDataException : Exception
{
    public InvalidTickDataException(string message) : base(message) { }
    public InvalidTickDataException(string message, Exception innerException) : base(message, innerException) { }
}


