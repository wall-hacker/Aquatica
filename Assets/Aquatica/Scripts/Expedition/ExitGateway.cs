using System;
using System.Collections;
using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// The way out: the same brick arch as the portal that brought the player here, grown big enough for the
    /// submarine to pass through. A seal across the opening (and a collider behind it) keeps it shut; three crystal
    /// clusters grow out of the stone, dormant and stone-like, and each wakes (blue glow, electric veins) as a Tide
    /// Crystal comes aboard. With all three the seal dissolves. The seal's glow
    /// lights its surroundings: a point light for lit objects (the arch, the submarine) and the global Aquatica glow
    /// for the seafloor and plants, both growing as it charges and opens. Raises <see cref="Crossed"/> when the
    /// submarine passes through the open gateway.
    /// </summary>
    public class ExitGateway : MonoBehaviour
    {
        static readonly int ChargeId = Shader.PropertyToID("_Charge");
        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int ActiveId = Shader.PropertyToID("_Active");
        static readonly int GlowPositionId = Shader.PropertyToID("_AquaticaGlowPosition");
        static readonly int GlowColorId = Shader.PropertyToID("_AquaticaGlowColor");

        [SerializeField] Renderer seal;
        [Tooltip("The crystal clusters on the arch, one per Tide Crystal (Aquatica/GateCrystal material).")]
        [SerializeField] Renderer[] sockets;
        [Tooltip("Seconds a crystal takes to wake.")]
        [SerializeField] float wakeTime = 2.5f;
        [Tooltip("Blocks the opening while the gateway is shut.")]
        [SerializeField] Collider barrier;
        [Tooltip("Colliders of the arch itself (put on the terrain layer).")]
        [SerializeField] Collider[] solid;
        [Tooltip("Uniform scale of the arch (the prefab is portal-sized).")]
        [SerializeField] float scale = 10.5f;
        [Tooltip("How far (m) the arch is sunk into the ground.")]
        [SerializeField] float sink = 1.5f;
        [SerializeField] float openDuration = 4.5f;

        [Header("Glow on the surroundings")]
        [SerializeField] Color glowColor = new(0.35f, 0.85f, 1f);
        [Tooltip("How far (m) the glow reaches.")]
        [SerializeField] float glowRange = 30f;
        [Tooltip("Glow strength shut, fully charged, and open.")]
        [SerializeField] Vector3 glowLevels = new(0.08f, 0.4f, 1.8f);
        [Tooltip("Point light intensity per unit of glow (lights the arch and the submarine).")]
        [SerializeField] float lightIntensity = 8f;

        public bool IsOpen { get; private set; }
        public bool IsOpening { get; private set; }

        /// <summary>The submarine has passed through the open gateway.</summary>
        public event Action Crossed;

        Material sealMaterial;
        MaterialPropertyBlock props;
        Transform watched;
        Bounds opening; // the seal's bounds in this transform's space
        float lastSide;
        bool crossed;
        float charge, chargeTarget, open;
        Light lamp;
        float[] socketActivity;
        int litSockets;

        /// <summary>Stands the gateway on the ground at a seafloor-local position, its opening facing along rotation.</summary>
        public void Place(Transform floor, Vector3 localPosition, Quaternion rotation, Transform submarine, int terrainLayer)
        {
            transform.SetParent(floor, false);
            transform.SetLocalPositionAndRotation(localPosition + Vector3.down * sink, rotation);
            transform.localScale = Vector3.one * scale;
            watched = submarine;
            foreach (var c in solid)
                if (c != null && terrainLayer >= 0)
                    c.gameObject.layer = terrainLayer;
            if (barrier != null && terrainLayer >= 0)
                barrier.gameObject.layer = terrainLayer;

            sealMaterial = seal.material;
            props = new MaterialPropertyBlock();
            socketActivity = new float[sockets.Length];
            var mesh = seal.GetComponent<MeshFilter>().sharedMesh.bounds;
            var toRoot = transform.worldToLocalMatrix * seal.transform.localToWorldMatrix;
            opening = new Bounds(toRoot.MultiplyPoint3x4(mesh.center), Vector3.zero);
            opening.Encapsulate(toRoot.MultiplyPoint3x4(mesh.min));
            opening.Encapsulate(toRoot.MultiplyPoint3x4(mesh.max));
            lastSide = Side();

            // The lamp hangs in the middle of the opening
            lamp = new GameObject("GlowLight").AddComponent<Light>();
            lamp.transform.SetParent(transform, false);
            // In the middle of the opening, a little towards the approach side so the arch's face catches it too
            lamp.transform.localPosition = opening.center + Vector3.forward * 0.25f;
            lamp.type = LightType.Point;
            lamp.color = glowColor;
            lamp.range = glowRange;
            lamp.shadows = LightShadows.None;
            SetCrystals(0);
        }

        /// <summary>Wakes one crystal cluster per Tide Crystal aboard.</summary>
        public void SetCrystals(int count)
        {
            litSockets = count;
            chargeTarget = sockets.Length > 0 ? Mathf.Clamp01(count / (float)sockets.Length) : 0f;
            UpdateSockets(0f);
        }

        void UpdateSockets(float dt)
        {
            for (int i = 0; i < sockets.Length; i++)
            {
                float target = i < litSockets ? 1f : 0f;
                socketActivity[i] = Mathf.MoveTowards(socketActivity[i], target, dt / Mathf.Max(0.01f, wakeTime));
                // Waking flickers on before it holds
                float k = socketActivity[i];
                float shown = k >= 1f || k <= 0f ? k : k * (0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 12f, i * 3.7f));
                sockets[i].GetPropertyBlock(props);
                props.SetFloat(ActiveId, shown);
                sockets[i].SetPropertyBlock(props);
            }
        }

        public void Open()
        {
            if (IsOpen || IsOpening)
                return;
            StartCoroutine(Opening());
        }

        IEnumerator Opening()
        {
            IsOpening = true;
            for (float t = 0f; t < openDuration; t += Time.deltaTime)
            {
                float k = t / openDuration;
                open = k * k * (3f - 2f * k);
                sealMaterial.SetFloat(OpenId, open);
                if (k > 0.4f && barrier != null)
                    barrier.enabled = false;
                yield return null;
            }
            open = 1f;
            sealMaterial.SetFloat(OpenId, 1f);
            if (barrier != null)
                barrier.enabled = false;
            IsOpening = false;
            IsOpen = true;
        }

        void Update()
        {
            if (sealMaterial == null)
                return;
            charge = Mathf.MoveTowards(charge, chargeTarget, Time.deltaTime * 0.5f);
            sealMaterial.SetFloat(ChargeId, charge);
            UpdateSockets(Time.deltaTime);
            UpdateGlow();

            if (!IsOpen || crossed || watched == null)
                return;
            float side = Side();
            if (Mathf.Sign(side) != Mathf.Sign(lastSide) && InOpening())
            {
                crossed = true;
                Crossed?.Invoke();
            }
            lastSide = side;
        }

        void UpdateGlow()
        {
            float level = Mathf.Lerp(Mathf.Lerp(glowLevels.x, glowLevels.y, charge), glowLevels.z, open);
            // A slow breath so it feels alive
            level *= 1f + 0.08f * Mathf.Sin(Time.time * 1.3f);
            lamp.intensity = level * lightIntensity;
            var centre = lamp.transform.position;
            Shader.SetGlobalVector(GlowPositionId, new Vector4(centre.x, centre.y, centre.z, glowRange));
            Shader.SetGlobalColor(GlowColorId, glowColor * level);
        }

        /// <summary>Signed distance of the submarine in front of (+) or behind (-) the opening's plane.</summary>
        float Side() => watched != null ? transform.InverseTransformPoint(watched.position).z - opening.center.z : 1f;

        bool InOpening()
        {
            var p = transform.InverseTransformPoint(watched.position);
            // A generous margin: the hull's centre is what's tested, the arch itself stops the rest
            return Mathf.Abs(p.x - opening.center.x) < opening.extents.x * 1.3f && p.y > opening.min.y - 1f && p.y < opening.max.y * 1.2f;
        }

        void OnDestroy()
        {
            if (sealMaterial != null)
                Destroy(sealMaterial);
            Shader.SetGlobalVector(GlowPositionId, Vector4.zero);
        }
    }
}
