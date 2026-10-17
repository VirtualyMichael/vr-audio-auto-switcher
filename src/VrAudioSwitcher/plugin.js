/**
 * @name VrAudioSwitcher
 * @description Lets the VR Audio Switcher desktop app set Discord's input/output device.
 * @version 0.1.0
 * @author VR Audio Switcher
 */
module.exports = class VrAudioSwitcher {
  start() {
    this.stopped = false;
    this.connect();
  }
  stop() {
    this.stopped = true;
    clearTimeout(this.retry);
    try { this.ws && this.ws.close(); } catch (e) {}
  }
  connect() {
    if (this.stopped) return;
    try {
      this.ws = new WebSocket("ws://127.0.0.1:__PORT__/?token=__TOKEN__");
      this.ws.onmessage = (m) => { try { this.apply(JSON.parse(m.data)); } catch (e) { console.error("[VrAudioSwitcher]", e); } };
      this.ws.onclose = () => { this.retry = setTimeout(() => this.connect(), 5000); };
      this.ws.onerror = () => { try { this.ws.close(); } catch (e) {} };
    } catch (e) { this.retry = setTimeout(() => this.connect(), 5000); }
  }
  norm(s) { return (s || "").toLowerCase().replace(/\s+/g, " ").trim(); }
  pick(devices, name, id) {
    const list = Array.isArray(devices) ? devices : Object.values(devices || {});
    const n = this.norm(name);
    return list.find(d => d.id === id)
        || list.find(d => this.norm(d.name) === n)
        || list.find(d => n && (this.norm(d.name).includes(n) || n.includes(this.norm(d.name))));
  }
  apply(msg) {
    if (msg.type !== "devices") return;
    const { Webpack } = BdApi;
    const actions = Webpack.getByKeys("setInputDevice", "setOutputDevice");
    const store = Webpack.getStore("MediaEngineStore");
    if (!actions || !store) { console.warn("[VrAudioSwitcher] Discord media modules not found"); return; }
    const i = this.pick(store.getInputDevices(), msg.inputName, msg.inputId);
    const o = this.pick(store.getOutputDevices(), msg.outputName, msg.outputId);
    if (i) actions.setInputDevice(i.id);
    if (o) actions.setOutputDevice(o.id);
  }
};
