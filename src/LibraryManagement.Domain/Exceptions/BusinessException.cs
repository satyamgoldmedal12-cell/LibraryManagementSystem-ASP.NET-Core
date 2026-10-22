using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Domain.Exceptions;

public sealed class BusinessException(string message) : Exception(message);
