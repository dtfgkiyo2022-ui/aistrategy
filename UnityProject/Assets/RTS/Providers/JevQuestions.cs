namespace Rts.Providers
{
    /// <summary>
    /// The questions sent with every state.
    /// <para>
    /// Version q1 asked "which objective should this faction prioritise" and "should the northern force retreat now".
    /// Measured over a match, the first answered "the enemy core" every time and the second sat at 0.3 whatever
    /// happened: a preference question has no answer the state can settle, and a question about a force that is named
    /// in the question rather than in the state cannot track the match. q2 asks instead about facts the state does
    /// settle - where the match is about to be decided, and whether the reserve is still worth holding - and each
    /// choice says which observable conditions pick it.
    /// </para>
    /// <para>
    /// The wording is versioned because a change of wording is a change of behaviour: in the measurements (#87) two
    /// phrasings of the same question moved the answers apart by 0.30. Answers are only comparable within a version.
    /// </para>
    /// </summary>
    public static class JevQuestions
    {
        public const string Version = "q6";

        // The keys are Jev's; the game's own names are mapped in HttpJevTransport.
        public const string Json =
            "{\"decisive_point\":{\"type\":\"choice\"," +
            "\"instructions\":\"この戦況で、次の1分間に勝敗を左右する場所はどこですか。state に書かれている観測だけで判断してください。\"," +
            "\"criteria\":{" +
            "\"north_outpost\":\"北の拠点。北の拠点が自分の所有でない、占領が進行中、または敵の目撃が北の拠点の近くにある\"," +
            "\"south_outpost\":\"南の拠点。南の拠点が自分の所有でない、占領が進行中、または敵の目撃が南の拠点の近くにある\"," +
            "\"my_core\":\"自分のコア。自分のコアのHPが減っている、または敵の目撃が自分のコアの近くにある\"," +
            "\"enemy_core\":\"敵のコア。自分の総兵数が敵の推定上限を上回り、敵の目撃が自分のコアから遠い\"}}," +
            "\"outnumbering\":{\"type\":\"noul\"," +
            "\"instructions\":\"自軍の総兵数（myTotalSoldiers）は、観測できている敵兵数の上限（enemy.knownSoldiersAtMost）より多い。\"}," +
            "\"enemy_near_my_core\":{\"type\":\"noul\"," +
            "\"instructions\":\"自分のコア（heldBy が me のコア）から40メートル以内に、敵の目撃（enemy.sightings）が少なくとも1つある。\"}," +
            "\"outpost_held_by_enemy\":{\"type\":\"noul\"," +
            "\"instructions\":\"objectives のうち kind が \\\"outpost\\\" である要素に限って見たとき、heldBy が \\\"the enemy\\\" であるものが少なくとも1つある。kind が \\\"core\\\" の要素は数えない。heldBy が \\\"nobody\\\" や \\\"unknown\\\" のものも数えない。\"}}";
    }
}
