﻿using CombatlogParser.Data;

namespace CombatlogParser.Tests
{
    public class UtilsTests
    {
        [Test]
        public void MillisToReadableString()
        {
            string resultA = ParsingUtil.MillisecondsToReadableTimeString(60_000);
            string resultB = ParsingUtil.MillisecondsToReadableTimeString(119_432);
            Assert.Multiple(() =>
            {
                Assert.That(resultA, Is.EqualTo("1:00"));
                Assert.That(resultB, Is.EqualTo("1:59"));
            });
        }

        [Test]
        public void MillisToReadableStringEnabledMillis()
        {
            string resultA = ParsingUtil.MillisecondsToReadableTimeString(60_000, true);
            string resultB = ParsingUtil.MillisecondsToReadableTimeString(119_432, true);
            Assert.Multiple(() =>
            {
                Assert.That(resultA, Is.EqualTo("1:00.000"));
                Assert.That(resultB, Is.EqualTo("1:59.432"));
            });
        }
    }
}
