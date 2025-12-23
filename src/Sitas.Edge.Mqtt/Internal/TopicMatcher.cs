namespace Sitas.Edge.Mqtt.Internal;

/// <summary>
/// Utility class for matching MQTT topic patterns with wildcards.
/// </summary>
internal static class TopicMatcher
{
    /// <summary>
    /// Checks if a topic matches a pattern with wildcards.
    /// </summary>
    /// <param name="pattern">The subscription pattern (may contain + and #).</param>
    /// <param name="topic">The actual topic to match against.</param>
    /// <returns>True if the topic matches the pattern.</returns>
    public static bool Matches(string pattern, string topic)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(topic))
            return false;

        // Exact match
        if (pattern == topic)
            return true;

        var patternParts = pattern.Split('/');
        var topicParts = topic.Split('/');

        return MatchParts(patternParts, topicParts, 0, 0);
    }

    private static bool MatchParts(string[] pattern, string[] topic, int patternIndex, int topicIndex)
    {
        // Both exhausted - match
        if (patternIndex >= pattern.Length && topicIndex >= topic.Length)
            return true;

        // Pattern exhausted but topic has more - no match
        if (patternIndex >= pattern.Length)
            return false;

        var patternPart = pattern[patternIndex];

        // Multi-level wildcard matches everything remaining
        if (patternPart == "#")
            return true;

        // Topic exhausted but pattern has more (and it's not #)
        if (topicIndex >= topic.Length)
            return false;

        var topicPart = topic[topicIndex];

        // Single-level wildcard matches exactly one level
        if (patternPart == "+")
            return MatchParts(pattern, topic, patternIndex + 1, topicIndex + 1);

        // Literal match required
        if (patternPart != topicPart)
            return false;

        return MatchParts(pattern, topic, patternIndex + 1, topicIndex + 1);
    }

    /// <summary>
    /// Validates a topic pattern.
    /// </summary>
    /// <param name="pattern">The pattern to validate.</param>
    /// <returns>True if the pattern is valid.</returns>
    public static bool IsValidPattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        var parts = pattern.Split('/');

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];

            // # must be the last character and the only character in its level
            if (part.Contains('#'))
            {
                if (part != "#" || i != parts.Length - 1)
                    return false;
            }

            // + must be the only character in its level
            if (part.Contains('+') && part != "+")
                return false;
        }

        return true;
    }
}

