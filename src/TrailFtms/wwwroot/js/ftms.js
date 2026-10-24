// Thin Web Bluetooth layer for the Fitness Machine Service (FTMS), and for Domyos bikes (proprietary
// protocol over a Microchip "transparent UART" service).
// Only moves bytes: all encoding and decoding lives in C# (FtmsProtocol, DomyosProtocol).

const FTMS_SERVICE = 0x1826;
const TREADMILL_DATA = 0x2acd;
const CONTROL_POINT = 0x2ad9;
const MACHINE_STATUS = 0x2ada;
const READABLE = [0x2acc, 0x2ad4, 0x2ad5]; // feature, speed range, inclination range

const DOMYOS_SERVICE = "49535343-fe7d-4ae5-8fa9-9fafd205e455";
const DOMYOS_NOTIFY = "49535343-1e4d-4bd9-ba61-23c647249616";
const DOMYOS_WRITE = "49535343-8841-43f4-a8d4-ecbe34729bb3";
const DOMYOS_CHUNK = 20; // bytes per write with the default MTU

const CONNECT_ATTEMPTS = 3;
const RETRY_DELAY_MS = 600;

let device = null;
let protocol = null; // "ftms" or "domyos"
let characteristics = new Map();

// The treadmill accepts a single connection: release it when the page goes away,
// otherwise a closed or reloaded tab can keep it busy and invisible to the next one.
window.addEventListener("pagehide", () => disconnect());

export async function getAvailability() {
    if (!navigator.bluetooth) return "unsupported";
    return (await navigator.bluetooth.getAvailability()) ? "available" : "unavailable";
}

// Opens the device picker and connects. Returns { name, protocol }, or null if the user cancelled.
export async function connect(dotnet) {
    try {
        device = await navigator.bluetooth.requestDevice({
            filters: [{ services: [FTMS_SERVICE] }, { namePrefix: "FS-" }, { namePrefix: "DOMYOS" }, { namePrefix: "Domyos" }],
            optionalServices: [FTMS_SERVICE, DOMYOS_SERVICE],
        });
    } catch (e) {
        if (e.name === "NotFoundError") return null; // picker closed
        throw e;
    }
    device.ongattserverdisconnected = null; // same device again: drop the handler of the previous session

    // GATT connection is often flaky on the first attempt (Windows, busy module): retry a few times.
    for (let attempt = 1; ; attempt++) {
        try {
            await setUp(dotnet);
            break;
        } catch (e) {
            if (device.gatt.connected) device.gatt.disconnect();
            if (attempt >= CONNECT_ATTEMPTS) throw e;
            await new Promise(resolve => setTimeout(resolve, RETRY_DELAY_MS * attempt));
        }
    }

    // Only now: a failed attempt above must not be reported as a lost connection.
    device.ongattserverdisconnected = () => dotnet.invokeMethodAsync("OnDisconnected");
    return { name: device.name ?? "", protocol };
}

async function setUp(dotnet) {
    const server = await device.gatt.connect();
    characteristics = new Map();
    let service;
    try {
        service = await server.getPrimaryService(FTMS_SERVICE);
    } catch {
        return setUpDomyos(dotnet, await server.getPrimaryService(DOMYOS_SERVICE));
    }

    protocol = "ftms";
    // Sequential on purpose: some Android stacks reject concurrent GATT operations.
    for (const uuid of [...READABLE, TREADMILL_DATA, CONTROL_POINT, MACHINE_STATUS]) {
        try {
            characteristics.set(uuid, await service.getCharacteristic(uuid));
        } catch {
            // Optional characteristic missing: C# handles nulls.
        }
    }

    // Indications on the Control Point must be enabled before any write.
    await subscribe(CONTROL_POINT, dotnet, "OnControlPoint", true);
    await subscribe(TREADMILL_DATA, dotnet, "OnTreadmillData");
    await subscribe(MACHINE_STATUS, dotnet, "OnMachineStatus");
}

async function setUpDomyos(dotnet, service) {
    protocol = "domyos";
    for (const uuid of [DOMYOS_NOTIFY, DOMYOS_WRITE])
        characteristics.set(uuid, await service.getCharacteristic(uuid));
    await subscribe(DOMYOS_NOTIFY, dotnet, "OnDomyosData", true);
}

export async function read(uuid) {
    const c = characteristics.get(uuid);
    return c ? toBytes(await c.readValue()) : null;
}

export async function write(bytes) {
    if (protocol === "domyos") {
        // A frame longer than one write goes in consecutive writes, the console joins them.
        const c = characteristics.get(DOMYOS_WRITE);
        for (let i = 0; i < bytes.length; i += DOMYOS_CHUNK)
            await c.writeValueWithResponse(bytes.slice(i, i + DOMYOS_CHUNK));
        return;
    }

    const c = characteristics.get(CONTROL_POINT);
    if (!c) throw new Error("Control Point not available");
    await c.writeValueWithResponse(bytes);
}

export function disconnect() {
    if (device?.gatt.connected) device.gatt.disconnect();
}

async function subscribe(uuid, dotnet, method, required = false) {
    const c = characteristics.get(uuid);
    if (!c) {
        if (required) throw new Error(`Characteristic 0x${uuid.toString(16)} not found`);
        return;
    }
    c.oncharacteristicvaluechanged = e => dotnet.invokeMethodAsync(method, toBytes(e.target.value));
    await c.startNotifications();
}

function toBytes(view) {
    return new Uint8Array(view.buffer.slice(view.byteOffset, view.byteOffset + view.byteLength));
}
