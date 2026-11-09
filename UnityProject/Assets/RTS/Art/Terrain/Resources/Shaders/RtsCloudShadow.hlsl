#ifndef RTS_CLOUD_SHADOW_INCLUDED
#define RTS_CLOUD_SHADOW_INCLUDED
// 11-10: soft cloud shadows drifting over the map. A pure function of the world position and the time, shared by the
// ground, nature, grass and water shaders. _RtsCloud (set by GraphicsQuality) is how much a cloud dims the sun;
// 0 turns it off. Display only.
float _RtsCloud;

float RtsCloudHash(float2 p)
{
    p = frac(p * float2(127.1, 311.7));
    p += dot(p, p + 34.23);
    return frac(p.x * p.y);
}

float RtsCloudNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(RtsCloudHash(i), RtsCloudHash(i + float2(1, 0)), u.x),
        lerp(RtsCloudHash(i + float2(0, 1)), RtsCloudHash(i + float2(1, 1)), u.x), u.y);
}

// 1 in sunlight, down to 1 - _RtsCloud under the middle of a cloud. Clouds are about 60-120 m across and drift
// about 3 m a second.
half RtsCloudLight(float3 world)
{
    if (_RtsCloud <= 0.001) return 1.0h;
    float2 p = world.xz * 0.011 + float2(_Time.y * 0.032, _Time.y * 0.018);
    float n = RtsCloudNoise(p) * 0.62 + RtsCloudNoise(p * 2.3 + 7.1) * 0.38;
    half cover = smoothstep(0.52, 0.70, n);
    return 1.0h - cover * _RtsCloud;
}
#endif
