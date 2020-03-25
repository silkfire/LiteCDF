namespace LiteCDF
{
    using System;


    /// <summary>
    /// Represents an error that occurs during the reading of a compound document.
    /// </summary>
    internal class CdfException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CdfException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public CdfException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CdfException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception or a <see langword="null"/> reference if no exception is specified.</param>
        public CdfException(string message, Exception innerException) : base(message, innerException) { }
    }
}
