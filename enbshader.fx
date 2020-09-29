float3 fColor0 : register(c0);
float3 fColor1 : register(c1);
float fChroma : register(c2);
float3 fGamma : register(c3);
float3 fMulti : register(c4);
float3 fShift : register(c5);
float3 fBloom : register(c6);
float2 fBlur : register(c7);
static const float3 fLuminance = {0.298912, 0.586611, 0.114478};
sampler sRT : register(s0);

float4 Color(float2 tex : TEXCOORD0) : COLOR0
{
	float3 result;
	result = tex2D(sRT, tex).rgb * (1.0 + fColor0 + fColor1);
	result = (result - dot(result, fLuminance)) * fChroma + dot(result, fLuminance);
	result = (pow(result, fGamma) - 0.5) * fMulti + 0.5 + fShift;
	return float4(result, 1.0);
}

float4 Press(float2 tex : TEXCOORD0) : COLOR0
{
	float3 result = {0.0, 0.0, 0.0};
/*	result += tex2D(sRT, tex + float2(-1.5, -1.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2(-0.5, -1.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2( 0.5, -1.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2( 1.5, -1.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2(-1.5, -0.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2(-0.5, -0.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2( 0.5, -0.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2( 1.5, -0.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2(-1.5,  0.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2(-0.5,  0.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2( 0.5,  0.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2( 1.5,  0.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2(-1.5,  1.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2(-0.5,  1.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2( 0.5,  1.5) * fBlur).rgb;
	result += tex2D(sRT, tex + float2( 1.5,  1.5) * fBlur).rgb;*/
	result = max(max(max(max(tex2D(sRT, tex + float2(-1.5, -1.5) * fBlur).rgb, tex2D(sRT, tex + float2(-0.5, -1.5) * fBlur).rgb),
		max(tex2D(sRT, tex + float2( 0.5, -1.5) * fBlur).rgb, tex2D(sRT, tex + float2( 1.5, -1.5) * fBlur).rgb)),
		max(max(tex2D(sRT, tex + float2(-1.5, -0.5) * fBlur).rgb, tex2D(sRT, tex + float2(-0.5, -0.5) * fBlur).rgb),
		max(tex2D(sRT, tex + float2( 0.5, -0.5) * fBlur).rgb, tex2D(sRT, tex + float2( 1.5, -0.5) * fBlur).rgb))),
		max(max(max(tex2D(sRT, tex + float2(-1.5,  0.5) * fBlur).rgb, tex2D(sRT, tex + float2(-0.5,  0.5) * fBlur).rgb),
		max(tex2D(sRT, tex + float2( 0.5,  0.5) * fBlur).rgb, tex2D(sRT, tex + float2( 1.5,  0.5) * fBlur).rgb)),
		max(max(tex2D(sRT, tex + float2(-1.5,  1.5) * fBlur).rgb, tex2D(sRT, tex + float2(-0.5,  1.5) * fBlur).rgb),
		max(tex2D(sRT, tex + float2( 0.5,  1.5) * fBlur).rgb, tex2D(sRT, tex + float2( 1.5,  1.5) * fBlur).rgb))));
	result = result * (1.0 + fColor0 + fColor1);
	result = (result - dot(result, fLuminance)) * fChroma + dot(result, fLuminance);
	result = (pow(result, fGamma) - 0.5) * fMulti + 0.5 + fShift;
	result = (result - dot(result, fLuminance)) * fBloom.y + dot(result, fLuminance);
	result = max(result + fBloom.x, float3(0.0, 0.0, 0.0));
	return float4(result, 1.0);
}

float4 Bloom(float2 tex : TEXCOORD0) : COLOR0
{
	float3 result = {0.0, 0.0, 0.0};

        result += tex2D(sRT, tex + float2(-200.0, -200.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-195.0, -195.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-190.0, -190.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-185.0, -185.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-180.0, -180.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-175.0, -175.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(-170.0, -170.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-165.0, -165.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-160.0, -160.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-155.0, -155.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-150.0, -150.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-145.0, -145.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-140.0, -140.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-135.0, -135.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-130.0, -130.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(-125.0, -125.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-120.0, -120.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-115.0, -115.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-110.0, -110.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-105.0, -105.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(-100.0, -100.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-95.0, -95.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-90.0, -90.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-85.0, -85.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(-80.0, -80.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-75.0, -75.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-70.0, -70.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-65.0, -65.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(-60.0, -60.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-55.0, -55.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-50.0, -50.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-45.0, -45.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(-40.0, -40.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-35.0, -35.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-30.0, -30.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-25.0, -25.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(-20.0, -20.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-15.0, -15.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-10.0, -10.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(-5.0,  -5.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(0.0,   0.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(5.0,   5.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(10.0,  10.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(15.0,  15.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(20.0,  20.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(25.0,  25.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(30.0,  30.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(35.0,  35.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(40.0,  40.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(45.0,  45.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(50.0,  50.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(55.0,  55.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(60.0,  60.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(65.0,  65.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(70.0,  70.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(75.0,  75.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(80.0,  80.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(85.0,  85.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(90.0,  90.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(95.0,  95.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(100.0,  100.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(105.0,  105.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(110.0,  110.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(115.0,  115.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(120.0,  120.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(125.0,  125.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(130.0,  130.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(135.0,  135.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(140.0,  140.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(145.0,  145.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(150.0,  150.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(155.0,  155.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(160.0,  160.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(165.0,  165.0) * fBlur).rgb * 0.1;
	result += tex2D(sRT, tex + float2(170.0,  170.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(175.0,  175.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(180.0,  180.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(185.0,  185.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(190.0,  190.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(195.0,  195.0) * fBlur).rgb * 0.1;
        result += tex2D(sRT, tex + float2(200.0,  200.0) * fBlur).rgb * 0.1;
        result = result * float3 (0.9, 0.85, 1.0);
	return float4(result * fBloom.z, 1.0);
}

float4 Blend(float2 tex : TEXCOORD0) : COLOR0
{
    return float4 (tex2D(sRT, tex).rgb, 1.0);
}
