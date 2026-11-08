using System;
using System.Collections.Generic;

namespace Kindling.Content
{
    public sealed class ContentException : Exception
    {
        public ContentException(IReadOnlyList<string> problems)
            : base("Content has " + problems.Count + " problem(s):\n- " + string.Join("\n- ", problems))
        {
            Problems = problems;
        }

        public IReadOnlyList<string> Problems { get; }
    }
}
