namespace Oloraculo.Web.WorldCup.Burden
{
    public static class WorldCupFixtureBurdenCalculator
    {
        public const string ModelVersion = "wc26-burden-v1";

        public static WorldCupFixtureBurdenScore Score(WorldCupFixtureBurdenInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (string.IsNullOrWhiteSpace(input.FixtureId))
                throw new ArgumentException("FixtureId is required.", nameof(input));

            var blockers = new List<string>();
            if (input.KickoffUtc is null)
                blockers.Add("NO_KICKOFF_UTC");
            if (string.IsNullOrWhiteSpace(input.Venue) && string.IsNullOrWhiteSpace(input.City))
                blockers.Add("NO_VENUE_OR_CITY");
            if (input.VenueExposure == WorldCupVenueExposure.Unknown)
                blockers.Add("NO_ROOF_OR_OPEN_AIR_STATE");
            if (input.ExpectedWbgtC is null)
                blockers.Add("NO_WBGT_ESTIMATE");
            if (input.PriorTravelKm is null)
                blockers.Add("NO_PRIOR_TRAVEL_KM");
            if (input.RestDaysBefore is null)
                blockers.Add("NO_REST_DAYS");
            if (input.AltitudeMeters is null)
                blockers.Add("NO_ALTITUDE_METERS");

            var heat = HeatScore(input.ExpectedWbgtC, input.LocalKickoffHour, input.VenueExposure);
            var travel = TravelScore(input.PriorTravelKm, input.EastwardTimeZoneShiftHours);
            var rest = RestScore(input.RestDaysBefore);
            var altitude = AltitudeScore(input.AltitudeMeters);
            var composite = Clamp01To100((0.40 * heat) + (0.25 * travel) + (0.20 * rest) + (0.15 * altitude));

            return new WorldCupFixtureBurdenScore
            {
                FixtureId = input.FixtureId,
                HeatScore = Math.Round(heat, 2),
                TravelScore = Math.Round(travel, 2),
                RestScore = Math.Round(rest, 2),
                AltitudeScore = Math.Round(altitude, 2),
                CompositeScore = Math.Round(composite, 2),
                Confidence = Confidence(blockers.Count),
                Blockers = blockers,
                Source = input.Source
            };
        }

        public static double HeatScore(double? expectedWbgtC, int? localKickoffHour, WorldCupVenueExposure exposure)
        {
            if (expectedWbgtC is null)
                return 0;

            var baseScore = expectedWbgtC.Value switch
            {
                < 18 => 0,
                < 22 => Scale(expectedWbgtC.Value, 18, 22, 0, 35),
                < 26 => Scale(expectedWbgtC.Value, 22, 26, 35, 75),
                _ => Scale(Math.Min(expectedWbgtC.Value, 30), 26, 30, 75, 100)
            };

            var exposureMultiplier = exposure switch
            {
                WorldCupVenueExposure.ClimateControlled => 0.15,
                WorldCupVenueExposure.RetractableRoof => 0.70,
                WorldCupVenueExposure.OpenAir => 1.00,
                _ => 0.80
            };
            var kickoffPenalty = localKickoffHour is >= 12 and <= 17 && exposure != WorldCupVenueExposure.ClimateControlled ? 10 : 0;
            return Clamp01To100((baseScore * exposureMultiplier) + kickoffPenalty);
        }

        public static double TravelScore(double? priorTravelKm, int? eastwardTimeZoneShiftHours)
        {
            var distanceScore = priorTravelKm is null ? 0 : Math.Min(100, Math.Max(0, priorTravelKm.Value) / 80.0);
            var timezonePenalty = eastwardTimeZoneShiftHours is null ? 0 : Math.Max(0, eastwardTimeZoneShiftHours.Value) * 5.0;
            return Clamp01To100(distanceScore + timezonePenalty);
        }

        public static double RestScore(double? restDaysBefore)
        {
            if (restDaysBefore is null)
                return 0;
            if (restDaysBefore.Value >= 5)
                return 0;
            return Clamp01To100((5 - Math.Max(0, restDaysBefore.Value)) * 20);
        }

        public static double AltitudeScore(int? altitudeMeters)
        {
            if (altitudeMeters is null || altitudeMeters.Value < 1000)
                return 0;
            return Clamp01To100(Scale(Math.Min(altitudeMeters.Value, 2200), 1000, 2200, 20, 100));
        }

        private static WorldCupBurdenConfidence Confidence(int blockerCount) => blockerCount switch
        {
            0 => WorldCupBurdenConfidence.High,
            <= 2 => WorldCupBurdenConfidence.Medium,
            _ => WorldCupBurdenConfidence.Low
        };

        private static double Scale(double value, double fromLow, double fromHigh, double toLow, double toHigh) =>
            toLow + ((value - fromLow) / (fromHigh - fromLow) * (toHigh - toLow));

        private static double Clamp01To100(double value) => Math.Clamp(value, 0, 100);
    }
}
