using System;

namespace Broiler.Media;

public class MediaException : Exception
{
    public MediaException(MediaError error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public MediaException(MediaError error, Exception innerException)
        : base(error?.Message, innerException)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public MediaError Error { get; }
}

