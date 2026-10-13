// Thin Web Bluetooth layer for the Fitness Machine Service (FTMS).
// Only moves bytes: all encoding and decoding lives in C# (FtmsProtocol).

const FTMS_SERVICE = 0x1826;
const TREADMILL_DATA = 0x2acd;
const CONTROL_POINT = 0x2ad9;
const MACHINE_STATUS = 0x2ada;
const READABLE = [0x2acc, 0x2ad4, 0x2ad5]; // feature, speed range, inclination range

let device = null;
let characteristics = new Map();

export async function getAvailability() {
    if (!navigator.bluetooth) return "unsupported";
    return (await navigator.bluetooth.getAvailability()) ? "available" : "unavailable";
}

// Opens the device picker and connects. Returns the device name, or null if the user cancelled.
export async function connect(dotnet) {
    try {
        device = await navigator.bluetooth.requestDevice({
            filters: [{ services: [FTMS_SERVICE] }, { namePrefix: "FS-" }],
            optionalServices: [FTMS_SERVICE],
        });
    } catch (e) {
        if (e.name === "NotFoundError") return null; // picker closed
        throw e;
    }

    device.ongattserverdisconnected = () => dotnet.invokeMethodAsync("OnDisconnected");

    const service = await (await device.gatt.connect()).getPrimaryService(FTMS_SERVICE);
    characteristics = new Map();
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

    return device.name ?? "";
}

export async function read(uuid) {
    const c = characteristics.get(uuid);
    return c ? toBytes(await c.readValue()) : null;
}

export async function write(bytes) {
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
