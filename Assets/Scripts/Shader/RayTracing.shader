Shader "Custom/RayTracing"
{
	SubShader
	{
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			#define FLT_MAX 3.402823466e+38F

			// Constants for the LCG and xorshift algorithms
			#define LCG_MULTIPLIER 747796405u
			#define LCG_INCREMENT 2891336453u
			#define XORSHIFT_MULTIPLIER 277803737u
			#define UINT_MAX_INV 2.3283064365386963e-10f // 1 / 4294967295.0
			#define TWO_PI 6.28318530718 // Predefine constant for 2π to avoid recalculating it repeatedly
			#define THRESHOLD_FOR_SKY_GRADIENT 0.4
			#define SMOOTHING_FACTOR 0.35
			#define GROUND_TO_SKY_THRESHOLD -0.05
			
			// --- Settings and constants ---
			static const float PI = 3.1415;

			// Raytracing Settings
			int MaxRayBounce;
			int RaysPerPixel;
			int Frame;

			// Camera Settings
			float FocusBlurAmount;
			float RaySpreadFactor;
			float3 CameraParameters;
			float4x4 CamLocalToWorldMatrix;

			// Environment Settings
			int EnvironmentEnabled;
			float4 GroundColour;
			float4 SkyColourHorizon;
			float4 SkyColour;
			float SunFocus;
			float SunIntensity;
			
			// Special material types
			static const int CheckerPattern = 1;
			static const int InvisibleLightSource = 2;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			
			// --- Structures ---
			struct Ray
			{
				float3 origin;
				float3 dir;
			};
			
			struct RayTracingMaterial
			{
				float4 colour;
				float4 emissionColour;
				float4 specularColour;
				float emissionStrength;
				float smoothness;
				float specular;
				int flag;
			};

			struct Triangle
			{
				float3 posA, posB, posC;
				float3 normalA, normalB, normalC;
			};

			struct meshData
			{
				uint firstTriangleIndex;
				uint numTriangles;
				RayTracingMaterial material;
				float3 boundsMin;
				float3 boundsMax;
			};

			struct HitInfo
			{
				bool didHit;
				float dst;
				float3 hitPoint;
				float3 normal;
				RayTracingMaterial material;
			};

			// --- Buffers ---	
			StructuredBuffer<Triangle> Triangles;
			StructuredBuffer<meshData> AllmeshData;
			int NumMeshes;

			// --- Ray Intersection Functions ---
			HitInfo RayTriangle(Ray ray, Triangle tri)
			{
				// Calculate edges from triangle vertices A to B and A to C
				float3 edgeAB = tri.posB - tri.posA;
				float3 edgeAC = tri.posC - tri.posA;

				// Cross product to get normal direction and setup for backface culling
				float3 normalDir = cross(edgeAB, edgeAC);

				// Vector from ray origin to triangle vertex A
				float3 ao = ray.origin - tri.posA;

				// Cross product of vector from A to ray origin and ray direction
				float3 dao = cross(ao, ray.dir);

				// Compute determinant to solve linear equations
				float determinant = -dot(ray.dir, normalDir);
				float invDet = 1 / determinant;
    
				// Calculate intersection point distance and barycentric coordinates
				float dst = dot(ao, normalDir) * invDet;
				float u = dot(edgeAC, dao) * invDet;
				float v = -dot(edgeAB, dao) * invDet;
				float w = 1 - u - v;
    
				// Check for valid intersection and fill hit info
				HitInfo hitInfo;
				hitInfo.didHit = determinant >= 1E-6 && dst >= 0 && u >= 0 && v >= 0 && w >= 0;
				hitInfo.hitPoint = ray.origin + ray.dir * dst;
				hitInfo.normal = normalize(tri.normalA * w + tri.normalB * u + tri.normalC * v);
				hitInfo.dst = dst;

				return hitInfo;
			}

			bool RayIntersectsBoundingBox(Ray ray, float3 boxMin, float3 boxMax)
			{
				// Handle division by zero in ray direction
				float3 invDir = ray.dir != 0 ? 1.0f / ray.dir : float3(FLT_MAX, FLT_MAX, FLT_MAX);

				// Calculate intersection times with the bounding box
				float3 tMin = (boxMin - ray.origin) * invDir;
				float3 tMax = (boxMax - ray.origin) * invDir;

				// Adjust for rays with negative directions
				float3 tEntry = min(tMin, tMax);
				float3 tExit = max(tMin, tMax);

				// Find the largest entry time and smallest exit time
				float tNear = max(max(tEntry.x, tEntry.y), tEntry.z);
				float tFar = min(min(tExit.x, tExit.y), tExit.z);

				// Determine if there is an intersection
				return tNear <= tFar && tFar > 0.0f; // Ensure tFar is positive for intersections in front of the ray origin
			};

			// --- RNG Stuff ---
			// Update the random state using a linear congruential generator and xorshift
			uint NextRandom(inout uint state)
			{
				// Linear congruential generator
				state = state * LCG_MULTIPLIER + LCG_INCREMENT;

				// Xorshift: XOR high bits of state, shifted right varying amounts
				uint xorshifted = ((state >> ((state >> 28) + 4)) ^ state) * XORSHIFT_MULTIPLIER;
				state = (xorshifted >> 22) ^ xorshifted;

				return state;
			}

			// Generates a random float between 0 and 1
			float RandomValue(inout uint state)
			{
				return NextRandom(state) * UINT_MAX_INV;
			}

			float RandomValueNormalDistribution(inout uint state)
			{
				// Generate a uniform random angle
				float theta = 2 * 3.1415926 * RandomValue(state); 

				// Generate radius using Box-Muller transform
				float rho = sqrt(-2 * log(RandomValue(state)));

				// Return a Gaussian random number with mean 0 and standard deviation 1
				return rho * cos(theta);
			}

			// Generates a random direction in 3D space
			float3 RandomDirection(inout uint state)
			{
				// Generating three normally distributed random values for each axis
				float x = RandomValueNormalDistribution(state);
				float y = RandomValueNormalDistribution(state);
				float z = RandomValueNormalDistribution(state);
				// Normalize to ensure the point is on the surface of the unit sphere
				return normalize(float3(x, y, z));
			}

			// Generates a random point within a unit circle using uniform distribution
			float2 RandomPointInCircle(inout uint rngState)
			{
				// Generate a random angle between 0 and 2π
				float angle = RandomValue(rngState) * TWO_PI;

				// Calculate the point on the unit circle for this angle
				float2 pointOnCircle = float2(cos(angle), sin(angle));

				// Scale the point within the circle by a random radius
				// The radius is squared to ensure uniform distribution of area (not just radius)
				return pointOnCircle * sqrt(RandomValue(rngState));
			}

			// Performs element-wise modulo operation between two 2D vectors.
			float2 mod2(float2 x, float2 y)
			{
				return x - y * floor(x / y);
			}

			// Calculates the color of the environment light based on the direction of the incoming ray.
			float3 GetEnvironmentLight(Ray ray)
			{
				// Early exit if environmental effects are disabled
				if (!EnvironmentEnabled) {
					return float3(0, 0, 0); // Return black color
				}

				// Calculate the transition factor for sky gradient based on ray's vertical component
				float skyGradientTransition = pow(smoothstep(0, THRESHOLD_FOR_SKY_GRADIENT, ray.dir.y), SMOOTHING_FACTOR);
				float groundToSkyTransition = smoothstep(GROUND_TO_SKY_THRESHOLD, 0, ray.dir.y);

				// Linearly interpolate between horizon and zenith colors based on the sky gradient
				float3 skyGradientColor = lerp(SkyColourHorizon, SkyColour, skyGradientTransition);

				// Calculate sunlight intensity based on the dot product of ray direction and the world space light position
				float sunlightIntensity = pow(max(0, dot(ray.dir, _WorldSpaceLightPos0.xyz)), SunFocus) * SunIntensity;

				// Combine the ground color with the sky gradient and add sunlight component
				float3 environmentalColor = lerp(GroundColour, skyGradientColor, groundToSkyTransition) + sunlightIntensity * (groundToSkyTransition >= 1);
				return environmentalColor;
			}

			// --- Ray Tracing Stuff ---

			// Find the first point that the given ray collides with, and return hit info
			HitInfo CalculateRayCollision(Ray ray)
			{
				HitInfo closestHit;
				// Initialize with infinitely far distance to ensure any hit is closer
				closestHit.dst = FLT_MAX;

				// Exit early if there are no meshes to process
				if (NumMeshes == 0) {
					return closestHit;
				}

				// Iterate over all meshes to find the closest hit
				for (int meshIndex = 0; meshIndex < NumMeshes; meshIndex++)
				{
					meshData currentMesh = AllmeshData[meshIndex];
					// Check if the ray intersects the bounding box of the current mesh
					if (!RayIntersectsBoundingBox(ray, currentMesh.boundsMin, currentMesh.boundsMax)) {
						continue;
					}

					// Check intersections with each triangle in the mesh
					for (uint i = 0; i < currentMesh.numTriangles; i++) {
						int triIndex = currentMesh.firstTriangleIndex + i;
						Triangle tri = Triangles[triIndex];
						HitInfo hitInfo = RayTriangle(ray, tri);

						// Update closest hit if this hit is closer
						if (hitInfo.didHit && hitInfo.dst < closestHit.dst) {
							closestHit = hitInfo;
							// Update material only when a new closest hit is found
							closestHit.material = currentMesh.material;
						}
					}
				}

				return closestHit;
			}

			float3 TraceRayPath(Ray ray, inout uint rngState)
			{
				float3 incomingLight = 0;
				float3 rayColour = 1;

				for (int bounceIndex = 0; bounceIndex <= MaxRayBounce; bounceIndex ++)
				{
					HitInfo hitInfo = CalculateRayCollision(ray);

					if (hitInfo.didHit)
					{
						RayTracingMaterial material = hitInfo.material;
						// Handle special material types:
						if (material.flag == CheckerPattern) 
						{
							float2 c = mod2(floor(hitInfo.hitPoint.xz), 2.0);
							material.colour = c.x == c.y ? material.colour : material.emissionColour;
						}
						else if (material.flag == InvisibleLightSource && bounceIndex == 0)
						{
							ray.origin = hitInfo.hitPoint + ray.dir * 0.001;
							continue;
						}

						// Figure out new ray position and direction
						bool isSpecularBounce = material.specular >= RandomValue(rngState);
					
						ray.origin = hitInfo.hitPoint;
						float3 diffuseDir = normalize(hitInfo.normal + RandomDirection(rngState));
						float3 specularDir = reflect(ray.dir, hitInfo.normal);
						ray.dir = normalize(lerp(diffuseDir, specularDir, material.smoothness * isSpecularBounce));

						// Update light calculations
						float3 emittedLight = material.emissionColour * material.emissionStrength;
						incomingLight += emittedLight * rayColour;
						rayColour *= lerp(material.colour, material.specularColour, isSpecularBounce);
						
						// Random early exit if ray colour is nearly 0 (can't contribute much to final result)
						float p = max(rayColour.r, max(rayColour.g, rayColour.b));
						if (RandomValue(rngState) >= p) {
							break;
						}
						rayColour *= 1.0f / p; 
					}
					else
					{
						incomingLight += GetEnvironmentLight(ray) * rayColour;
						break;
					}
				}

				return incomingLight;
			}
		
			// Run for every pixel in the display
			float4 frag(v2f i) : SV_Target
			{
				// Constants for calculations
				uint2 numPixels = _ScreenParams.xy;
				float invNumPixelsX = 1.0 / numPixels.x;  // Inverse of screen width for performance
				float3 camRight = CamLocalToWorldMatrix._m00_m10_m20;  // Camera right vector
				float3 camUp = CamLocalToWorldMatrix._m01_m11_m21;      // Camera up vector

				// Calculate focus point only once per fragment
				float3 focusPointLocal = float3(i.uv - 0.5, 1) * CameraParameters;
				float3 focusPoint = mul(CamLocalToWorldMatrix, float4(focusPointLocal, 1));

				float3 totalIncomingLight = 0;
				uint pixelIndex = (uint)(i.uv.y * numPixels.y) * numPixels.x + (uint)(i.uv.x * numPixels.x);
				uint rngState = pixelIndex + Frame * 719393;  // Seed for RNG

				for (int rayIndex = 0; rayIndex < RaysPerPixel; rayIndex++)
				{
					// Compute jitter for defocus blur and ray spread
					float2 defocusJitter = RandomPointInCircle(rngState) * FocusBlurAmount * invNumPixelsX;
					float2 jitter = RandomPointInCircle(rngState) * RaySpreadFactor * invNumPixelsX;

					// Calculate ray origin and direction considering the jitter
					Ray ray;
					ray.origin = _WorldSpaceCameraPos + camRight * defocusJitter.x + camUp * defocusJitter.y;
					float3 jitteredFocusPoint = focusPoint + camRight * jitter.x + camUp * jitter.y;
					ray.dir = normalize(jitteredFocusPoint - ray.origin);

					// Trace the ray and accumulate the light
					totalIncomingLight += TraceRayPath(ray, rngState);
				}

				// Average the accumulated light by the number of rays per pixel
				float3 pixelColor = totalIncomingLight / RaysPerPixel;
				return float4(pixelColor, 1);
			}
			ENDCG
		}
	}
}