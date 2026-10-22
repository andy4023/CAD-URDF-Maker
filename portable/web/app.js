import * as THREE from './vendor/three.module.js';
import { OrbitControls } from './vendor/OrbitControls.js';
import { identity, multiply, inverse, point, vector, normalize, fromRpy, rpy, forward, ellipsoid, linkFrame, alignmentTransform, axesAlignment, jointAxis, storedAxis } from './math.js?v=1.3.4.0';
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = (n, d = 6) => Number.isFinite(n) ? Number(n.toPrecision(d)).toString() : '—';
const degrees = (rad) => rad * 180 / Math.PI;
const radians = (deg) => deg * Math.PI / 180;
const uid = () => crypto.randomUUID();
let project = null, properties = {}, meshUrls = {}, busy = false;
let selection = { type: 'project', id: '' };
let positions = {}, search = '', cachedSelection = [];
let viewer = null;
let originErrors = {}, assignedMassKg = 0;
let alignmentUp = '+Z', alignmentForward = '+X';
let checkedUnassigned = new Set(), removedOpen = false;
function send(command) { const bridge = window.chrome?.webview; if (bridge)
    bridge.postMessage(command);
else
    toast('Connect this editor through CAD-URDF Maker in SolidWorks.', 'error'); }
function toast(message, kind = '') { $('message').textContent = message; $('message').className = kind; }
function mutate(action) { const before = structuredClone(project); try {
    action();
    send({ type: 'update', project });
    render();
}
catch (error) {
    project = before;
    toast(error.message, 'error');
} }
function select(type, id = '') { selection = { type, id }; renderStructure(); renderInspector(); viewer?.highlight(); }
function field(label, id, value, type = 'text', extra = '') { return `<label class="field">${esc(label)}<input id="${id}" type="${type}" value="${esc(value)}" ${extra}></label>`; }
function options(items, chosen) { return items.map(x => `<option value="${esc(x.Id)}" ${x.Id === chosen ? 'selected' : ''}>${esc(x.Name)}</option>`).join(''); }
function number(id) { const element = $(id); if (element.value.trim() === '')
    throw new Error('Complete all numeric fields.'); const v = Number(element.value); if (!Number.isFinite(v))
    throw new Error('Enter a finite number.'); return v; }
function value(id) { return ($(id)).value; }
function bind(id, event, action) { $(id)?.addEventListener(event, action); }
function vectorFields(label, prefix, v) { const labels = prefix.endsWith('rpy') ? ['Roll', 'Pitch', 'Yaw'] : ['X', 'Y', 'Z']; return `<label class="field">${esc(label)}</label><div class="row">${v.map((x, i) => field(labels[i], prefix + i, fmt(x, 12), 'number', 'step="any"')).join('')}</div>`; }
function readVector(prefix) { return [0, 1, 2].map(i => number(prefix + i)); }
function physicsPanel(p, cogLabel = 'CoG · m') { if (!p)
    return '<p>Properties unavailable until components are mapped.</p>'; return `<div class="property-line"><span>Mass</span><strong>${fmt(p.Mass, 10)} kg</strong></div>${cogPanel(p.Cog, cogLabel)}<label class="field">Inertia at CoG · kg·m²</label><div class="tensor">${p.Inertia.map(x => `<span>${fmt(x, 8)}</span>`).join('')}</div><p class="source">${esc(p.Source)}</p>`; }
function linkFrameSummary(link) { const root = project.Links.find(l => l.Id === project.RootLink), p = properties[link.Id]; if (!root)
    return ''; let html = `<p>Axes: ${project.ChildFramesFollowRoot !== false ? 'same orientation as ' + esc(root.Name) + ' at zero pose' : 'individual link orientation'}. Origin: this link's confirmed origin on its parent joint axis (root and fixed links use their chosen origin).</p>`; if (p && link.Id !== root.Id) {
    const cog = point(multiply(inverse(linkFrame(project, root)), linkFrame(project, link)), p.Cog);
    html += `<details class="comparison"><summary>Root-frame comparison</summary>${cogPanel(cog, 'CoG · root origin, m')}</details>`;
} return html; }
function cogPanel(cog, label) { return `<div class="cog-line"><span>${esc(label)}</span><div class="cog-coordinates">${cog.map((n, i) => `<span><small>${'XYZ'[i]}</small><strong>${fmt(n, 7)}</strong></span>`).join('')}</div></div>`; }
function ownLink(componentId) { return project.Links.find(l => l.Components.includes(componentId)); }
function uniqueName(base, names) { let name = base, index = 2; while (names.includes(name))
    name = base + '_' + index++; return name; }
function checkedName(raw, kind, id = '') {
    const name = raw.trim();
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(name))
        throw new Error('Use letters, digits and underscores; start with a letter or underscore.');
    if (kind === 'robot' && /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/i.test(name))
        throw new Error('This name is reserved by Windows. Choose another robot name.');
    if (kind === 'joint' && project.Joints.some(j => j.Id !== id && j.Name === name))
        throw new Error('Another joint already uses this name. Joint names must be unique.');
    return name;
}
function bindJointName(inputId, buttonId, joint) {
    const apply = () => { try {
        const name = checkedName(value(inputId), 'joint', joint.Id);
        mutate(() => project.Joints.find(j => j.Id === joint.Id).Name = name);
        toast('Joint name updated.', 'success');
    }
    catch (error) {
        toast(error.message, 'error');
    } };
    bind(buttonId, 'click', apply);
    $(inputId)?.addEventListener('keydown', (event) => { if (event.key === 'Enter') {
        event.preventDefault();
        apply();
    } });
}
function robotNamingPanel() { return `<section id="robot-naming"><h3>Robot & export name</h3>${field('Robot name', 'robot-name', project.Name)}<button id="rename-robot" ${busy ? 'disabled' : ''}>Apply robot name</button><p>URDF file: <strong>${esc(project.Name)}.urdf</strong><br>Export folder: <strong>${esc(project.Name)}/</strong></p><p>Use letters, digits and underscores; start with a letter or underscore.</p></section>`; }
function newLink(ids = []) { const l = { Id: uid(), Name: uniqueName('link', project.Links.map(l => l.Name)), Components: ids, ZeroFrame: ids.length ? project.Components.find(c => c.Id === ids[0]).ZeroTransform.slice() : identity(), Origin: originDraft(), ShowFrame: false }; project.Links.push(l); if (!project.RootLink)
    project.RootLink = l.Id; return l; }
function render() {
    if (!project)
        return;
    const status = $('status');
    status.textContent = busy ? 'Reading CAD…' : !project.ZeroCaptured ? 'Capture zero pose' : project.Stale ? 'CAD refresh needed' : 'CAD snapshot ready';
    status.className = 'pill ' + (project.Stale ? 'stale' : 'ready');
    $('assembly').textContent = project.AssemblyTitle ? project.AssemblyTitle + ' · ' + project.Configuration : 'Native CAD · SI properties · portable URDF';
    $('cancel').hidden = !busy;
    document.querySelectorAll('button[data-command]').forEach(b => b.disabled = busy && b.dataset.command !== 'cancel');
    $('mass-total').textContent = fmt(assignedMassKg, 8) + ' kg · assigned mass';
    const frameFlags = project.Links.map(l => l.ShowFrame);
    const frameControl = $('frames');
    frameControl.checked = frameFlags.length > 0 && frameFlags.every(Boolean);
    frameControl.indeterminate = frameFlags.some(Boolean) && !frameControl.checked;
    renderStructure();
    renderSuggestions();
    renderInspector();
    renderSliders();
    viewer?.sync();
}
function renderStructure() {
    if (!project)
        return;
    let html = '';
    for (const link of project.Links) {
        const components = link.Components.map(id => project.Components.find(c => c.Id === id)).filter(Boolean);
        if (search && !link.Name.toLowerCase().includes(search) && !components.some(c => c.Name.toLowerCase().includes(search)))
            continue;
        html += `<button class="link-row ${selection.type === 'link' && selection.id === link.Id ? 'active' : ''}" data-select="link" data-id="${esc(link.Id)}"><span class="icon">◇</span><span class="label">${esc(link.Name)}</span>${!link.Origin?.Confirmed ? '<span class="badge draft">ORIGIN DRAFT</span>' : ''}${link.Id === project.RootLink ? '<span class="badge">ROOT</span>' : `<span class="count">${components.length}</span>`}</button>`;
        for (const joint of project.Joints.filter(j => j.Child === link.Id))
            html += `<div class="joint-row ${selection.type === 'joint' && selection.id === joint.Id ? 'active' : ''}" data-select="joint" data-id="${esc(joint.Id)}">↳ ${esc(joint.Name)} ${joint.Confirmed ? '' : '· review'}</div>`;
        for (const c of components)
            html += componentButton(c, false);
    }
    const allUnassigned = project.Components.filter(c => !c.Excluded && !ownLink(c.Id)), unassigned = allUnassigned.filter(c => !search || c.Name.toLowerCase().includes(search));
    checkedUnassigned = new Set([...checkedUnassigned].filter(id => allUnassigned.some(c => c.Id === id)));
    if (project.Components.length) {
        html += `<div class="section-head"><h2>Unassigned</h2><span class="count">${allUnassigned.length}</span></div><div class="scope-actions"><span>${checkedUnassigned.size} selected${search ? ' · ' + unassigned.length + ' shown' : ''}</span><div><button id="select-visible-unassigned" ${!unassigned.length || busy ? 'disabled' : ''}>Select all shown</button><button id="clear-unassigned-selection" ${!checkedUnassigned.size || busy ? 'disabled' : ''}>Clear</button></div><button id="remove-unassigned" class="danger" ${!checkedUnassigned.size || busy ? 'disabled' : ''}>Remove selected from URDF</button><button id="remove-cad-unassigned" ${!allUnassigned.length || busy ? 'disabled' : ''}>Remove CAD selection from URDF</button><p>Remaining unassigned components block export.</p></div>`;
        html += unassigned.map(c => `<div class="scope-row"><input type="checkbox" data-check-unassigned="${esc(c.Id)}" aria-label="Select ${esc(c.Name)} for removal" ${checkedUnassigned.has(c.Id) ? 'checked' : ''} ${busy ? 'disabled' : ''}>${componentButton(c, true)}</div>`).join('');
        if (!unassigned.length)
            html += `<p class="hint">${allUnassigned.length ? 'No unassigned components match this search.' : 'No unassigned components.'}</p>`;
    }
    const removed = project.Components.filter(c => c.Excluded && !ownLink(c.Id) && (!search || c.Name.toLowerCase().includes(search)));
    if (removed.length)
        html += `<details id="removed-components" class="removed-components" ${removedOpen ? 'open' : ''}><summary>Removed / excluded <span class="count">${removed.length}</span></summary><p class="hint">Excluded from the URDF. CAD components are retained.</p><button id="restore-removed" ${busy ? 'disabled' : ''}>Restore all shown</button>${removed.map(c => componentButton(c, true)).join('')}</details>`;
    $('structure').innerHTML = html || '<p class="hint">Capture the CAD assembly, then create links from its components.</p>';
    $('structure').querySelectorAll('[data-select]').forEach(el => el.onclick = () => select(el.dataset.select, el.dataset.id));
    $('structure').querySelectorAll('[data-check-unassigned]').forEach(el => el.onchange = () => { if (el.checked)
        checkedUnassigned.add(el.dataset.checkUnassigned);
    else
        checkedUnassigned.delete(el.dataset.checkUnassigned); renderStructure(); });
    bind('select-visible-unassigned', 'click', () => { unassigned.forEach(c => checkedUnassigned.add(c.Id)); renderStructure(); });
    bind('clear-unassigned-selection', 'click', () => { checkedUnassigned.clear(); renderStructure(); });
    bind('remove-unassigned', 'click', () => send({ type: 'remove-unassigned', ids: [...checkedUnassigned] }));
    bind('remove-cad-unassigned', 'click', () => send({ type: 'remove-unassigned-cad' }));
    bind('restore-removed', 'click', () => send({ type: 'restore-components', ids: removed.map(c => c.Id) }));
    const removedDetails = $('removed-components');
    if (removedDetails)
        removedDetails.addEventListener('toggle', () => removedOpen = removedDetails.open);
}
function componentButton(c, orphan) { return `<button class="component-row ${orphan ? 'orphan' : ''} ${selection.type === 'component' && selection.id === c.Id ? 'active' : ''}" data-select="component" data-id="${esc(c.Id)}" title="${esc(c.Name)}"><span class="dot ${!c.Resolved || c.Issue ? 'bad' : ''}"></span><span class="label">${esc(c.Name)}</span>${c.Excluded ? '<span class="badge">OUT</span>' : ''}</button>`; }
function renderSuggestions() {
    if (!project)
        return;
    const suggestions = project.Suggestions.map((s, i) => ({ s, i })).filter(({ s }) => [s.ComponentA, s.ComponentB].every(id => project.Components.some(c => c.Id === id && !c.Excluded)));
    $('suggestion-count').textContent = String(suggestions.length);
    $('suggestions').innerHTML = suggestions.slice(0, 100).map(({ s, i }) => { const a = project.Components.find(c => c.Id === s.ComponentA), b = project.Components.find(c => c.Id === s.ComponentB); return `<div class="suggestion"><strong>${esc(a?.Name)} ↔ ${esc(b?.Name)}</strong><p>${esc(s.Reason)}</p><button data-suggestion="${i}">${s.Type === 'fixed' ? 'Group rigid components' : 'Create joint candidate'}</button></div>`; }).join('') || '<p class="hint">Mate candidates appear after CAD capture. You can also define links and joints explicitly.</p>';
    $('suggestions').querySelectorAll('[data-suggestion]').forEach(b => b.onclick = () => useSuggestion(Number(b.dataset.suggestion)));
}
function useSuggestion(index) {
    mutate(() => {
        const s = project.Suggestions[index];
        let a = ownLink(s.ComponentA), b = ownLink(s.ComponentB);
        if (s.Type === 'fixed') {
            if (a && b && a.Id !== b.Id)
                throw new Error('Both components already belong to different links. Merge their memberships explicitly.');
            const l = a || b || newLink();
            for (const id of [s.ComponentA, s.ComponentB]) {
                const c = project.Components.find(c => c.Id === id);
                c.Excluded = false;
                if (!l.Components.includes(id))
                    l.Components.push(id);
            }
            selection = { type: 'link', id: l.Id };
        }
        else {
            a = a || newLink([s.ComponentA]);
            b = b || newLink([s.ComponentB]);
            if (a.Id === b.Id)
                throw new Error('These components are in one rigid link. Split them before adding a joint.');
            if (project.Joints.some(j => j.Child === b.Id))
                throw new Error('Child link already has a parent. Edit that joint or reverse parent/child in a new joint.');
            if (s.Point) {
                b.ZeroFrame[3] = s.Point[0];
                b.ZeroFrame[7] = s.Point[1];
                b.ZeroFrame[11] = s.Point[2];
            }
            const j = createJoint(a.Id, b.Id);
            j.Type = s.Type;
            if (s.Direction)
                j.Axis = normalize(vector(inverse(b.ZeroFrame), s.Direction));
            selection = { type: 'link', id: b.Id };
        }
    });
}
function createJoint(parent, child) { let base = (project.Links.find(l => l.Id === child)?.Name || 'link').replace(/[^A-Za-z0-9_]/g, '_').replace(/_link$/, '') || 'link'; if (!/^[A-Za-z_]/.test(base))
    base = '_' + base; const j = { Id: uid(), Name: uniqueName(base + '_joint', project.Joints.map(j => j.Name)), Type: 'revolute', Parent: parent, Child: child, Axis: storedAxis(project, child, [0, 0, 1]), Lower: -Math.PI / 2, Upper: Math.PI / 2, Effort: 0, Velocity: 0, Confirmed: false, GeometryReference: '', GeometryComponent: '', ReferencePoint: null, ReferenceDirection: null, ReferenceValid: true }; project.Joints.push(j); return j; }
function renderInspector() {
    if (!project)
        return;
    const panel = $('inspector');
    if (selection.type === 'project')
        return renderProject();
    if (selection.type === 'component') {
        const c = project.Components.find(c => c.Id === selection.id);
        if (!c) {
            selection = { type: 'project', id: '' };
            return renderProject();
        }
        $('inspector-title').textContent = 'Component properties';
        const effective = c.Override.Mode === 'complete' ? c.Override.Complete : c.Override.Mode === 'density' && c.UnitDensity ? { ...c.UnitDensity, Mass: c.UnitDensity.Mass * c.Override.Density, Inertia: c.UnitDensity.Inertia.map(x => x * c.Override.Density), Source: 'Tool density override' } : c.Cad;
        panel.innerHTML = `<strong>${esc(c.Name)}</strong><p class="readonly-path">${esc(c.Path)}<br>${esc(c.Configuration)}</p>${c.Issue ? `<p class="warn">${esc(c.Issue)}</p>` : ''}<label class="field">Assigned link<select id="component-link"><option value="">Unassigned</option>${options(project.Links, ownLink(c.Id)?.Id)}</select></label><label class="check"><input type="checkbox" id="excluded" ${c.Excluded ? 'checked' : ''}> Explicitly exclude from robot</label>${!c.MaterialKnown ? `<label class="check"><input type="checkbox" id="material-ack" ${c.MaterialAcknowledged ? 'checked' : ''}> I verified this CAD material/default density assumption</label>` : ''}<label class="field">Physical property source<select id="override-mode">${['cad', 'density', 'complete'].map(m => `<option value="${m}" ${m === c.Override.Mode ? 'selected' : ''}>${{ cad: 'Effective CAD properties', density: 'Uniform density override', complete: 'Complete physical override' }[m]}</option>`).join('')}</select></label>${c.Override.Mode === 'density' ? field('Density · kg/m³', 'density', c.Override.Density, 'number', 'step="any"') : ''}${c.Override.Mode === 'complete' ? field('Mass · kg', 'override-mass', c.Override.Complete.Mass, 'number', 'step="any"') + vectorFields('CoG in component coordinates · m', 'override-cog', c.Override.Complete.Cog) + `<h3>Tensor at component CoG · kg·m²</h3><div class="row">${[0, 4, 8].map((index, i) => field(['Ixx', 'Iyy', 'Izz'][i], 'inertia' + index, c.Override.Complete.Inertia[index], 'number', 'step="any"')).join('')}</div><div class="row">${[1, 2, 5].map((index, i) => field(['Ixy', 'Ixz', 'Iyz'][i], 'inertia' + index, c.Override.Complete.Inertia[index], 'number', 'step="any"')).join('')}</div><p>Enter tensor off-diagonals, including their signs.</p>` : ''}${c.Override.Mode !== 'cad' ? `<label class="field">Source / measurement note<textarea id="override-note">${esc(c.Override.Note)}</textarea></label><button id="apply-override">Apply override</button>` : ''}<h3>Effective properties · component axes</h3>${physicsPanel(effective)}<div class="actions">${!ownLink(c.Id) ? `<button id="scope-component" class="${c.Excluded ? "" : "danger"}" ${busy ? "disabled" : ""}>${c.Excluded ? "Restore to unassigned" : "Remove from URDF"}</button>` : ""}<button id="remap">Remap from CAD selection</button></div><p>For a replacement component, refresh first and select it in CAD. Review affected joint references after remapping.</p>`;
        bind('component-link', 'change', () => mutate(() => { project.Links.forEach(l => l.Components = l.Components.filter(id => id !== c.Id)); const l = project.Links.find(l => l.Id === value('component-link')); if (l) {
            l.Components.push(c.Id);
            c.Excluded = false;
        } }));
        bind('excluded', 'change', () => mutate(() => { c.Excluded = $('excluded').checked; if (c.Excluded)
            project.Links.forEach(l => l.Components = l.Components.filter(id => id !== c.Id)); }));
        bind('material-ack', 'change', () => mutate(() => c.MaterialAcknowledged = $('material-ack').checked));
        bind('override-mode', 'change', () => mutate(() => { c.Override.Mode = value('override-mode'); if (c.Override.Mode === 'complete' && c.Override.Complete.Mass === 0)
            c.Override.Complete = structuredClone(c.Cad); }));
        bind('apply-override', 'click', () => mutate(() => { if (c.Override.Mode === 'density')
            c.Override.Density = number('density'); if (c.Override.Mode === 'complete') {
            const i = new Array(9).fill(0);
            for (const index of [0, 1, 2, 4, 5, 8])
                i[index] = number('inertia' + index);
            i[3] = i[1];
            i[6] = i[2];
            i[7] = i[5];
            c.Override.Complete = { Mass: number('override-mass'), Cog: readVector('override-cog'), Inertia: i, Source: 'Tool complete override' };
        } c.Override.Note = value('override-note'); }));
        bind('remap', 'click', () => send({ type: 'remap', id: c.Id }));
        bind('scope-component', 'click', () => send({ type: c.Excluded ? 'restore-components' : 'remove-unassigned', ids: [c.Id] }));
        return;
    }
    if (selection.type === 'link')
        return renderLink();
    if (selection.type === 'joint')
        renderJoint();
}
function originDraft() { return { Mode: 'draft', Confirmed: false, ReferenceValid: true, JointId: '', PointReference: '', PointComponent: '', LocalPoint: null, SourcePoint: null, AxisPoint: null, AxisDirection: null, OffsetMetres: 0 }; }
function originInput(link) { if (link.Id === project.RootLink)
    return identity(); const joint = project.Joints.find(j => j.Child === link.Id); if (!joint)
    throw new Error('Choose the parent joint first.'); const parent = project.Links.find(l => l.Id === joint.Parent); if (!parent)
    throw new Error('Choose an existing parent link.'); return linkFrame(project, parent); }
function originPanel(link) {
    const o = link.Origin, j = project.Joints.find(j => j.Child === link.Id), root = link.Id === project.RootLink, moving = !root && j && j.Type !== 'fixed';
    let html = `<h3>3. Link origin <span class="badge ${o.Confirmed ? '' : 'draft'}">${o.Confirmed ? 'CONFIRMED' : 'DRAFT'}</span></h3><label class="check"><input id="show-origin-frame" type="checkbox" ${link.ShowFrame ? 'checked' : ''}> Show origin frame</label>`;
    if (!root && !j)
        return html + '<p class="hint">Choose a parent joint before defining this link origin.</p>';
    const frame = originInput(link), position = point(inverse(frame), [link.ZeroFrame[3], link.ZeroFrame[7], link.ZeroFrame[11]]), basis = root ? 'captured CAD' : project.Links.find(l => l.Id === j.Parent)?.Name;
    html += `<p>Origin coordinates use ${esc(basis)} axes and origin. ${project.ChildFramesFollowRoot !== false ? 'Link axes match the root at zero pose.' : ''}</p>`;
    if (o.Confirmed)
        return html + cogPanel(position, 'Confirmed origin · m') + '<button id="begin-origin">Edit origin</button>';
    const anchor = point(inverse(frame), o.AxisPoint || [link.ZeroFrame[3], link.ZeroFrame[7], link.ZeroFrame[11]]), direction = vector(inverse(frame), o.AxisDirection || (j ? vector(link.ZeroFrame, j.Axis) : [0, 0, 1]));
    html += '<button id="begin-origin">Define origin / show frame</button>';
    if (moving)
        html += `<h3>Joint axis</h3><button id="origin-axis">1. Use selected CAD axis / cylinder</button><p>${o.AxisDirection ? 'Axis defined. Pick a CAD vertex, sketch point, or reference point to locate the origin along it.' : 'Select exactly one reference axis or cylindrical face in SolidWorks.'}</p><details><summary>Numeric axis</summary>${vectorFields('Point on axis · m', 'origin-axis-point', anchor)}${vectorFields('Axis direction', 'origin-axis-direction', direction)}<button id="origin-numeric-axis">Define numeric axis</button></details>`;
    html += `<h3>Origin point</h3><button id="origin-point">${moving ? '2. Project selected CAD point onto axis' : 'Use selected CAD vertex / sketch point / reference point'}</button><p>${moving ? 'The selected point is projected onto the axis; an off-axis vertex sets the axial station.' : 'Select exactly one vertex, sketch point, or reference point in SolidWorks.'}</p>${vectorFields('Numeric origin · m', 'origin-position', position)}<button id="origin-position">Apply numeric origin</button>`;
    if (moving)
        html += field('Offset along selected axis · mm', 'origin-offset', o.OffsetMetres * 1000, 'number', 'step="any"') + '<button id="apply-origin-offset">Preview offset</button>';
    html += `<p class="${originErrors[link.Id] ? 'warn' : 'quiet'}">${esc(originErrors[link.Id] || 'Review the preview, then confirm the origin. CoG and inertia will update automatically.')}</p><button class="primary" id="confirm-origin">Confirm origin</button>`;
    return html;
}
function renderLink() {
    const link = project.Links.find(l => l.Id === selection.id);
    if (!link) {
        selection = { type: 'project', id: '' };
        return renderProject();
    }
    const panel = $('inspector'), j = project.Joints.find(j => j.Child === link.Id), root = link.Id === project.RootLink;
    $('inspector-title').textContent = 'Link definition';
    panel.innerHTML = field('Link name', 'link-name', link.Name) + `<label class="check"><input id="root-link" type="checkbox" ${root ? 'checked' : ''}> Root link</label><h3>1. Rigid components</h3>${link.Components.map(id => `<div class="member"><span>${esc(project.Components.find(c => c.Id === id)?.Name || 'Missing component')}</span><button data-remove-component="${esc(id)}">Remove</button></div>`).join('')}<button id="use-selection">Add selected CAD components</button><label class="field">Add from captured assembly<select id="add-component"><option value="">Choose component…</option>${options(project.Components.filter(c => !ownLink(c.Id)), '')}</select></label><h3>2. Parent joint</h3>${root ? '<p>Root link — choose its origin directly.</p>' : `${j ? field('Joint name', 'link-joint-name', j.Name) + '<button id="rename-link-joint">Apply joint name</button>' : ''}<label class="field">Parent link<select id="new-parent">${options(project.Links.filter(l => l.Id !== link.Id), j?.Parent || project.RootLink)}</select></label><label class="field">Joint type<select id="link-joint-type">${['revolute', 'continuous', 'prismatic', 'fixed'].map(type => `<option ${type === (j?.Type || 'revolute') ? 'selected' : ''}>${type}</option>`).join('')}</select></label><button id="${j ? 'edit-joint' : 'add-parent-joint'}">${j ? 'Edit joint limits / direction' : 'Create parent joint'}</button>`}${originPanel(link)}<h3>4. Automatic inertial data</h3>${properties[link.Id] ? physicsPanel(properties[link.Id], 'CoG · this link origin, m') + linkFrameSummary(link) : `<p class="origin-pending">${esc(link.Origin.Confirmed ? (originErrors[link.Id] || 'Assign resolved components with valid physical properties to calculate inertia.') : 'Define and confirm the link origin to calculate its CoG and inertia. Assigned mass remains available in the footer.')}</p>`}<button id="delete-link" class="danger">Delete link</button>`;
    bind('link-name', 'change', () => mutate(() => link.Name = value('link-name')));
    bind('root-link', 'change', () => mutate(() => { if ($('root-link').checked)
        project.RootLink = link.Id; }));
    bind('show-origin-frame', 'change', () => mutate(() => link.ShowFrame = $('show-origin-frame').checked));
    bind('use-selection', 'click', () => send({ type: 'selection' }));
    bind('add-component', 'change', () => mutate(() => { const id = value('add-component'); if (id) {
        link.Components.push(id);
        project.Components.find(c => c.Id === id).Excluded = false;
    } }));
    panel.querySelectorAll('[data-remove-component]').forEach(button => button.onclick = () => mutate(() => link.Components = link.Components.filter(id => id !== button.dataset.removeComponent)));
    bind('add-parent-joint', 'click', () => mutate(() => { const parent = value('new-parent'); if (!parent)
        throw new Error('Choose an existing parent link.'); const joint = createJoint(parent, link.Id); joint.Type = value('link-joint-type'); }));
    bind('new-parent', 'change', () => { if (j)
        mutate(() => { j.Parent = value('new-parent'); j.Confirmed = false; }); });
    bind('link-joint-type', 'change', () => { if (j)
        mutate(() => { j.Type = value('link-joint-type'); j.Confirmed = false; }); });
    bind('edit-joint', 'click', () => select('joint', j.Id));
    if (j)
        bindJointName('link-joint-name', 'rename-link-joint', j);
    bind('begin-origin', 'click', () => send({ type: 'origin-begin', id: link.Id }));
    bind('origin-axis', 'click', () => send({ type: 'origin-axis', id: link.Id }));
    bind('origin-point', 'click', () => send({ type: 'origin-point', id: link.Id }));
    bind('confirm-origin', 'click', () => send({ type: 'origin-confirm', id: link.Id }));
    bind('origin-position', 'click', () => { try {
        send({ type: 'origin-position', id: link.Id, point: readVector('origin-position') });
    }
    catch (error) {
        toast(error.message, 'error');
    } });
    bind('origin-numeric-axis', 'click', () => { try {
        send({ type: 'origin-numeric-axis', id: link.Id, point: readVector('origin-axis-point'), direction: readVector('origin-axis-direction') });
    }
    catch (error) {
        toast(error.message, 'error');
    } });
    bind('apply-origin-offset', 'click', () => { try {
        send({ type: 'origin-offset', id: link.Id, offsetMetres: number('origin-offset') / 1000 });
    }
    catch (error) {
        toast(error.message, 'error');
    } });
    bind('delete-link', 'click', () => { if (confirm('Delete this link and its connected joints?'))
        mutate(() => { project.Links = project.Links.filter(l => l.Id !== link.Id); project.Joints = project.Joints.filter(j => j.Parent !== link.Id && j.Child !== link.Id); if (root)
            project.RootLink = project.Links[0]?.Id || ''; selection = { type: 'project', id: '' }; }); });
}
function alignmentPanel() { const alignment = project.Alignment || { Enabled: false, Rpy: [0, 0, 0] }, axes = ['+X', '-X', '+Y', '-Y', '+Z', '-Z']; const choices = (selected) => axes.map(a => `<option ${selected === a ? 'selected' : ''}>${a}</option>`).join(''); return `<section id="model-alignment"><h3>Cartesian alignment</h3><label class="check"><input id="child-frames-root" type="checkbox" ${project.ChildFramesFollowRoot !== false ? 'checked' : ''}> Child axes match root at zero pose</label><p>Joint origins stay in place. CoG coordinates remain relative to each link origin.</p><p>${alignment.Enabled ? 'Aligned robot frame · +Z up, +X forward, +Y left' : 'Using original captured coordinates'}</p><div class="row"><label class="field">CAD direction → up (+Z)<select id="alignment-up">${choices(alignmentUp)}</select></label><label class="field">CAD direction → forward (+X)<select id="alignment-forward">${choices(alignmentForward)}</select></label></div><button id="align-directions">Align directions</button><p>Choose two different CAD axes. For a tilted assembly, enter a precise rotation below.</p>${vectorFields('Model rotation · degrees', 'alignment-rpy', alignment.Rpy.map(degrees))}<button id="apply-alignment">Apply rotation</button><div class="actions">${['X', 'Y', 'Z'].map((axis, i) => `<button data-turn="${i}" data-sign="-1">${axis} −90°</button><button data-turn="${i}" data-sign="1">${axis} +90°</button>`).join('')}</div><button id="reset-alignment">Restore captured orientation</button><p>Rotation uses fixed X, then Y, then Z axes. The aligned root origin stays at its captured location and becomes (0, 0, 0) in the robot preview and URDF. Component properties remain auditable in CAD axes. Save the project to keep alignment; no CAD refresh is needed.</p></section>`; }
function setAlignment(angles, enabled = true) { if (busy)
    return toast('Wait for CAD capture to finish.', 'error'); if (!project.ZeroCaptured)
    return toast('Capture zero pose before aligning the model.', 'error'); if (angles.length !== 3 || angles.some(x => !Number.isFinite(x)))
    return toast('Enter three finite rotation angles.', 'error'); if (!enabled) {
    alignmentUp = '+Z';
    alignmentForward = '+X';
} mutate(() => { project.Alignment = { Enabled: enabled, Rpy: angles }; }); toast(enabled ? 'Cartesian alignment applied to preview and export.' : 'Captured orientation restored.', 'success'); viewer?.fit(); }
function bindAlignment() { bind('child-frames-root', 'change', () => { if (busy)
    return; mutate(() => project.ChildFramesFollowRoot = $('child-frames-root').checked); }); bind('alignment-up', 'change', () => alignmentUp = value('alignment-up')); bind('alignment-forward', 'change', () => alignmentForward = value('alignment-forward')); bind('align-directions', 'click', () => { try {
    const direction = (name) => [0, 1, 2].map(i => 'XYZ'[i] === name[1] ? (name[0] === '-' ? -1 : 1) : 0);
    setAlignment(rpy(axesAlignment(direction(alignmentUp), direction(alignmentForward))));
}
catch (error) {
    toast(error.message, 'error');
} }); bind('apply-alignment', 'click', () => { try {
    setAlignment(readVector('alignment-rpy').map(radians));
}
catch (error) {
    toast(error.message, 'error');
} }); bind('reset-alignment', 'click', () => setAlignment([0, 0, 0], false)); $('model-alignment').querySelectorAll('[data-turn]').forEach(button => button.onclick = () => { const turn = [0, 0, 0]; turn[Number(button.dataset.turn)] = Number(button.dataset.sign) * Math.PI / 2; const current = project.Alignment?.Enabled ? project.Alignment.Rpy : [0, 0, 0]; setAlignment(rpy(multiply(fromRpy([0, 0, 0], turn), fromRpy([0, 0, 0], current)))); }); }
function renderProject() {
    const panel = $('inspector');
    $('inspector-title').textContent = 'Project settings';
    panel.innerHTML = robotNamingPanel() + alignmentPanel() + `<label class="field">Root link<select id="project-root"><option value="">Choose root…</option>${options(project.Links, project.RootLink)}</select></label><h3>CAD snapshot</h3><p>${esc(project.AssemblyTitle || 'No assembly captured')}<br>Configuration: ${esc(project.Configuration || '—')}</p><p>Zero pose captured: ${project.CapturedUtc ? esc(new Date(project.CapturedUtc).toLocaleString()) : '—'}</p><p class="readonly-path">${esc(project.AssemblyPath)}</p><h3>STL export quality</h3>${field('Chord deviation · mm', 'chord', project.Export.ChordMetres * 1000, 'number', 'step="any"')}${field('Angular tolerance · degrees', 'angle', degrees(project.Export.AngleRadians), 'number', 'step="any"')}<button id="apply-settings">Apply mesh settings</button><p>Changing quality requires Refresh CAD. Preview meshes are reduced separately; inertia always comes from CAD geometry.</p><h3>Workflow</h3><p>Capture → group components → parent joint → define and confirm link origin → automatic inertia → limits → validate/export.</p><p>Use CAD selection for components and joint axes. The robot preview moves independently of the assembly.</p><button id="all-links">Create one link per component</button><p>This is an editable starting point. Group rigid components before exporting.</p>${project.CadIssues.map(issue => `<p class="warn">${esc(issue)}</p>`).join('')}`;
    bindAlignment();
    const renameRobot = () => { try {
        const name = checkedName(value('robot-name'), 'robot');
        mutate(() => project.Name = name);
        toast('Robot name updated. Export file: ' + name + '.urdf', 'success');
    }
    catch (error) {
        toast(error.message, 'error');
    } };
    bind('rename-robot', 'click', renameRobot);
    $('robot-name').addEventListener('keydown', (event) => { if (event.key === 'Enter') {
        event.preventDefault();
        renameRobot();
    } });
    bind('project-root', 'change', () => mutate(() => project.RootLink = value('project-root')));
    bind('apply-settings', 'click', () => mutate(() => { const chord = number('chord') / 1000, angle = radians(number('angle')); if (chord <= 0 || angle <= 0 || angle >= Math.PI)
        throw new Error('Use positive mesh tolerances, with angle below 180°.'); project.Export = { ChordMetres: chord, AngleRadians: angle }; project.Stale = true; }));
    bind('all-links', 'click', () => mutate(() => { for (const c of project.Components.filter(c => !c.Excluded && !ownLink(c.Id)))
        newLink([c.Id]); }));
}
function renderJoint() {
    const j = project.Joints.find(j => j.Id === selection.id);
    if (!j) {
        selection = { type: 'project', id: '' };
        return renderProject();
    }
    const parent = project.Links.find(l => l.Id === j.Parent), child = project.Links.find(l => l.Id === j.Child);
    const local = parent && child ? multiply(inverse(linkFrame(project, parent)), linkFrame(project, child)) : identity();
    const angular = j.Type !== 'prismatic';
    $('inspector-title').textContent = 'Joint definition';
    $('inspector').innerHTML = field('Joint name', 'joint-name', j.Name) + `<button id="rename-joint" ${busy ? 'disabled' : ''}>Apply joint name</button><p>Joint names are saved in the URDF and must be unique.</p><label class="field">Type<select id="joint-type">${['fixed', 'revolute', 'continuous', 'prismatic'].map(t => `<option value="${t}" ${j.Type === t ? 'selected' : ''}>${t}</option>`).join('')}</select></label><div class="row"><label class="field">Parent<select id="joint-parent">${options(project.Links, j.Parent)}</select></label><label class="field">Child<select id="joint-child">${options(project.Links, j.Child)}</select></label></div><h3>Child-link origin</h3>${cogPanel([local[3], local[7], local[11]], 'Origin relative to parent · m')}<p>${child?.Origin?.Confirmed ? 'Origin confirmed' : 'Origin draft — define and confirm before inertia/export'}. ${project.ChildFramesFollowRoot !== false ? 'Axes match root at zero pose.' : ''}</p><button id="define-child-origin">Define child link origin</button>${j.Type !== 'fixed' ? `<h3>Axis in joint frame</h3>${vectorFields('Direction', 'joint-axis', jointAxis(project, j))}<div class="actions"><button id="apply-axis">Normalize & apply</button><button id="reverse-axis">Reverse direction</button><button id="cad-axis">Use selected CAD axis / face</button></div><p>CAD selection also places the joint origin on the selected axis. For an axis with no physical endpoint, adjust the origin along it if needed.</p><h3>Motion limits</h3>${j.Type !== 'continuous' ? `<div class="row">${field('Lower · ' + (angular ? 'degrees' : 'm'), 'lower', fmt(angular ? degrees(j.Lower) : j.Lower, 12), 'number', 'step="any"')}${field('Upper · ' + (angular ? 'degrees' : 'm'), 'upper', fmt(angular ? degrees(j.Upper) : j.Upper, 12), 'number', 'step="any"')}</div>` : ''}<div class="row">${field('Effort · ' + (angular ? 'N·m' : 'N'), 'effort', j.Effort, 'number', 'step="any"')}${field('Velocity · ' + (angular ? 'rad/s' : 'm/s'), 'velocity', j.Velocity, 'number', 'step="any"')}</div><button id="apply-limits">Apply limits</button>` : ''}${!j.ReferenceValid ? '<p class="warn">CAD joint geometry changed or disappeared. Select it again or explicitly apply a numeric axis/frame.</p>' : ''}<label class="check"><input id="joint-confirmed" type="checkbox" ${j.Confirmed ? 'checked' : ''} ${!j.ReferenceValid ? 'disabled' : ''}> I verified the frame, axis, direction, and permitted motion</label><button id="delete-joint" class="danger">Delete joint</button>`;
    const invalidate = () => { j.Confirmed = false; };
    bindJointName('joint-name', 'rename-joint', j);
    bind('joint-type', 'change', () => mutate(() => { j.Type = value('joint-type'); invalidate(); }));
    for (const name of ['parent', 'child'])
        bind('joint-' + name, 'change', () => mutate(() => { j[name === 'parent' ? 'Parent' : 'Child'] = value('joint-' + name); invalidate(); }));
    bind('define-child-origin', 'click', () => select('link', j.Child));
    bind('apply-axis', 'click', () => { try {
        const l = project.Links.find(l => l.Id === j.Child), input = originInput(l), anchor = l.Origin.AxisPoint || [l.ZeroFrame[3], l.ZeroFrame[7], l.ZeroFrame[11]];
        send({ type: 'origin-numeric-axis', id: l.Id, point: point(inverse(input), anchor), direction: vector(inverse(input), vector(linkFrame(project, l), readVector('joint-axis'))) });
    }
    catch (error) {
        toast(error.message, 'error');
    } });
    bind('reverse-axis', 'click', () => mutate(() => { j.Axis = j.Axis.map(x => -x); [j.Lower, j.Upper] = [-j.Upper, -j.Lower]; positions[j.Id] = -(positions[j.Id] || 0); invalidate(); }));
    bind('cad-axis', 'click', () => send({ type: 'axis', id: j.Id }));
    bind('apply-limits', 'click', () => mutate(() => { if (j.Type !== 'continuous') {
        const lower = number('lower'), upper = number('upper');
        if (lower > upper || lower > 0 || upper < 0)
            throw new Error('Limits must be ordered and include zero.');
        j.Lower = angular ? radians(lower) : lower;
        j.Upper = angular ? radians(upper) : upper;
        positions[j.Id] = Math.max(j.Lower, Math.min(j.Upper, positions[j.Id] || 0));
    } j.Effort = number('effort'); j.Velocity = number('velocity'); if (j.Effort <= 0 || j.Velocity <= 0)
        throw new Error('Effort and velocity must be positive.'); }));
    bind('joint-confirmed', 'change', () => mutate(() => j.Confirmed = $('joint-confirmed').checked));
    bind('delete-joint', 'click', () => mutate(() => { project.Joints = project.Joints.filter(x => x.Id !== j.Id); selection = { type: 'link', id: j.Child }; }));
}
function renderSliders() {
    const joints = project.Joints.filter(j => j.Type !== 'fixed');
    $('sliders').innerHTML = joints.map(j => { const unit = j.Type === 'prismatic' ? 'm' : '°', lower = j.Type === 'continuous' ? -Math.PI : j.Lower, upper = j.Type === 'continuous' ? Math.PI : j.Upper; const display = (v) => j.Type === 'prismatic' ? v : degrees(v); return `<div class="slider-row" data-joint="${esc(j.Id)}"><span class="name" title="${esc(j.Name)}">${esc(j.Name)}</span><input aria-label="${esc(j.Name)} position" type="range" min="${lower}" max="${upper}" step="${Math.max(1e-8, (upper - lower) / 2000)}" value="${positions[j.Id] || 0}"><input aria-label="${esc(j.Name)} position in ${unit}" title="${unit}" type="number" step="any" value="${fmt(display(positions[j.Id] || 0), 7)}"></div>`; }).join('') || '<p class="quiet">Add a joint to explore its motion.</p>';
    $('sliders').querySelectorAll('[data-joint]').forEach(row => { const j = project.Joints.find(j => j.Id === row.dataset.joint); const slider = row.querySelector('input[type=range]'), input = row.querySelector('input[type=number]'); row.querySelector('.name').onclick = () => select('joint', j.Id); slider.oninput = () => { positions[j.Id] = Number(slider.value); input.value = fmt(j.Type === 'prismatic' ? positions[j.Id] : degrees(positions[j.Id]), 8); viewer?.pose(); }; input.oninput = () => { if (input.value === '')
        return; let n = Number(input.value); if (!Number.isFinite(n))
        return; if (j.Type !== 'prismatic')
        n = radians(n); const min = j.Type === 'continuous' ? -Math.PI : j.Lower, max = j.Type === 'continuous' ? Math.PI : j.Upper; positions[j.Id] = Math.max(min, Math.min(max, n)); slider.value = String(positions[j.Id]); viewer?.pose(); }; input.onchange = () => input.value = fmt(j.Type === 'prismatic' ? positions[j.Id] : degrees(positions[j.Id]), 8); });
}
class RobotViewer {
    constructor() {
        this.groups = new Map();
        this.geometry = new Map();
        this.generation = 0;
        this.frames = {};
        this.fitNext = true;
        this.frameCount = 0;
        this.lastStats = performance.now();
        this.meshSignature = '';
        const element = $('viewport');
        this.scene = new THREE.Scene();
        this.camera = new THREE.PerspectiveCamera(40, 1, .001, 1000);
        this.camera.up.set(0, 0, 1);
        this.camera.position.set(1.4, -2, 1.1);
        this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
        this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
        this.renderer.outputColorSpace = THREE.SRGBColorSpace;
        element.prepend(this.renderer.domElement);
        this.controls = new OrbitControls(this.camera, this.renderer.domElement);
        this.controls.enableDamping = true;
        this.controls.target.set(0, 0, .45);
        this.root = new THREE.Group();
        this.overlay = new THREE.Group();
        this.scene.add(this.root, this.overlay);
        this.scene.add(new THREE.HemisphereLight(0xf9fff6, 0x6a7366, 2.5));
        const light = new THREE.DirectionalLight(0xffffff, 3);
        light.position.set(3, -4, 5);
        this.scene.add(light);
        const grid = new THREE.GridHelper(4, 40, 0xc7d3c5, 0xe1e8dd);
        grid.rotation.x = Math.PI / 2;
        this.scene.add(grid);
        this.scene.add(new THREE.AxesHelper(.15));
        new ResizeObserver(() => { const w = element.clientWidth, h = element.clientHeight; this.camera.aspect = w / h; this.camera.updateProjectionMatrix(); this.renderer.setSize(w, h); }).observe(element);
        this.renderer.domElement.addEventListener('click', e => { if (Math.abs(e.movementX) + Math.abs(e.movementY) > 4)
            return; const bounds = this.renderer.domElement.getBoundingClientRect(); const mouse = new THREE.Vector2((e.clientX - bounds.left) / bounds.width * 2 - 1, -(e.clientY - bounds.top) / bounds.height * 2 + 1); const ray = new THREE.Raycaster(); ray.setFromCamera(mouse, this.camera); const hit = ray.intersectObjects(this.root.children, true)[0]; if (hit) {
            const id = hit.object.userData.component;
            if (id)
                select('component', id);
        } });
        const loop = () => { requestAnimationFrame(loop); this.controls.update(); this.renderer.render(this.scene, this.camera); this.frameCount++; const now = performance.now(); if (now - this.lastStats > 1500) {
            $('render-stats').textContent = Math.round(this.frameCount * 1000 / (now - this.lastStats)) + ' FPS · ' + this.renderer.info.render.triangles.toLocaleString() + ' preview triangles';
            this.frameCount = 0;
            this.lastStats = now;
        } };
        loop();
    }
    matrix(values) { return new THREE.Matrix4().set(...values); }
    clear(group) { while (group.children.length) {
        const child = group.children[0];
        group.remove(child);
        child.traverse((o) => { if (o.userData.ownsGeometry)
            o.geometry?.dispose(); if (o.material) {
            if (Array.isArray(o.material))
                o.material.forEach((m) => m.dispose());
            else
                o.material.dispose();
        } });
    } }
    async sync() {
        if (!project)
            return;
        const signature = project.Components.filter(c => !c.Excluded && meshUrls[c.Id]).map(c => c.Id + ':' + meshUrls[c.Id]).sort().join('|');
        if (signature === this.meshSignature && this.groups.size === project.Components.filter(c => !c.Excluded && meshUrls[c.Id]).length) {
            this.pose();
            return;
        }
        this.meshSignature = signature;
        const generation = ++this.generation;
        this.clear(this.root);
        this.clear(this.overlay);
        this.groups.clear();
        $('empty').style.display = project.Components.length ? 'none' : 'flex';
        const used = new Set(Object.keys(meshUrls));
        for (const [id, entry] of this.geometry)
            if (!used.has(id)) {
                entry.geometry.dispose();
                this.geometry.delete(id);
            }
        const jobs = project.Components.filter(c => !c.Excluded && meshUrls[c.Id]);
        let index = 0;
        const load = async () => {
            while (index < jobs.length) {
                const c = jobs[index++], url = meshUrls[c.Id];
                try {
                    let entry = this.geometry.get(c.Id);
                    if (!entry || entry.url !== url) {
                        const response = await fetch(url);
                        if (!response.ok)
                            throw new Error('Mesh unavailable');
                        const buffer = await response.arrayBuffer();
                        const data = new DataView(buffer);
                        if (buffer.byteLength < 84)
                            throw new Error('Invalid STL');
                        const count = data.getUint32(80, true);
                        if (buffer.byteLength !== 84 + count * 50)
                            throw new Error('Invalid STL length');
                        const positions = new Float32Array(count * 9);
                        for (let i = 0; i < count; i++)
                            for (let k = 0; k < 9; k++)
                                positions[i * 9 + k] = data.getFloat32(84 + i * 50 + 12 + k * 4, true);
                        const geometry = new THREE.BufferGeometry();
                        geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
                        geometry.computeVertexNormals();
                        if (generation !== this.generation) {
                            geometry.dispose();
                            return;
                        }
                        entry?.geometry.dispose();
                        entry = { url, geometry };
                        this.geometry.set(c.Id, entry);
                    }
                    if (generation !== this.generation)
                        return;
                    const material = new THREE.MeshStandardMaterial({ color: 0xabbdb3, roughness: .63, metalness: .16, side: THREE.DoubleSide });
                    const mesh = new THREE.Mesh(entry.geometry, material);
                    mesh.userData.component = c.Id;
                    mesh.matrixAutoUpdate = false;
                    this.root.add(mesh);
                    this.groups.set(c.Id, mesh);
                    this.pose();
                }
                catch (error) {
                    toast(c.Name + ': ' + error.message, 'error');
                }
            }
        };
        await Promise.all([load(), load(), load()]);
        if (generation !== this.generation)
            return;
        this.pose();
        if (this.fitNext) {
            this.fit();
            this.fitNext = false;
        }
        $('preview-subtitle').textContent = this.groups.size + ' component instances · captured zero + joint motion';
    }
    pose() {
        if (!project)
            return;
        const started = performance.now();
        try {
            this.frames = forward(project, positions);
            $('viewer-error').hidden = true;
        }
        catch (error) {
            this.frames = {};
            $('viewer-error').hidden = false;
            $('viewer-error').textContent = 'Preview has an unresolved joint tree: ' + error.message;
        }
        for (const c of project.Components) {
            const mesh = this.groups.get(c.Id);
            if (!mesh)
                continue;
            const link = ownLink(c.Id);
            const t = link && this.frames[link.Id] ? multiply(multiply(this.frames[link.Id], inverse(linkFrame(project, link))), c.ZeroTransform) : multiply(alignmentTransform(project), c.ZeroTransform);
            mesh.matrix.copy(this.matrix(t));
            mesh.matrixWorldNeedsUpdate = true;
        }
        this.highlight();
        this.drawOverlays();
        $('viewport').dataset.poseMs = String(performance.now() - started);
    }
    highlight() { if (!project)
        return; const link = selection.type === 'link' ? selection.id : selection.type === 'joint' ? project.Joints.find(j => j.Id === selection.id)?.Child : null; for (const [id, mesh] of this.groups) {
        const selected = selection.type === 'component' ? selection.id === id : link === ownLink(id)?.Id;
        mesh.material.color.setHex(selected ? 0x2c9478 : 0xabbdb3);
        mesh.material.emissive.setHex(selected ? 0x082b20 : 0x000000);
    } }
    drawOverlays() {
        this.clear(this.overlay);
        const showFrames = $('frames').checked, showCog = $('cogs').checked, showInertia = $('ellipsoids').checked;
        for (const l of project.Links) {
            const t = this.frames[l.Id] || multiply(alignmentTransform(project), linkFrame(project, l));
            if (l.ShowFrame) {
                const axes = new THREE.AxesHelper(.06);
                axes.matrixAutoUpdate = false;
                axes.matrix.copy(this.matrix(t));
                axes.userData.ownsGeometry = true;
                if (!l.Origin?.Confirmed) {
                    axes.material.transparent = true;
                    axes.material.opacity = .4;
                }
                this.overlay.add(axes);
            }
            const p = properties[l.Id];
            if (p && showCog) {
                const sphere = new THREE.Mesh(new THREE.SphereGeometry(.004, 10, 8), new THREE.MeshBasicMaterial({ color: 0xd79a3d, depthTest: false }));
                sphere.position.fromArray(point(t, p.Cog));
                sphere.userData.ownsGeometry = true;
                this.overlay.add(sphere);
            }
            if (p && showInertia && p.Mass > 0) {
                const e = ellipsoid(p);
                const sphere = new THREE.Mesh(new THREE.SphereGeometry(1, 16, 12), new THREE.MeshBasicMaterial({ color: 0xca9d52, transparent: true, opacity: .16, wireframe: true }));
                const b = e.basis;
                const local = [b[0], b[1], b[2], p.Cog[0], b[3], b[4], b[5], p.Cog[1], b[6], b[7], b[8], p.Cog[2], 0, 0, 0, 1];
                sphere.matrixAutoUpdate = false;
                sphere.matrix.copy(this.matrix(multiply(t, local)).multiply(new THREE.Matrix4().makeScale(...e.radii)));
                sphere.userData.ownsGeometry = true;
                this.overlay.add(sphere);
            }
        }
        for (const j of project.Joints.filter(j => j.Type !== 'fixed')) {
            const t = this.frames[j.Child] || project.Links.find(l => l.Id === j.Child)?.ZeroFrame;
            if (!t || j.Axis.length !== 3)
                continue;
            try {
                const direction = normalize(vector(t, jointAxis(project, j)));
                const arrow = new THREE.ArrowHelper(new THREE.Vector3(...direction), new THREE.Vector3(t[3], t[7], t[11]), .07, selection.id === j.Id ? 0xd27632 : 0x67899d, .017, .008);
                arrow.userData.ownsGeometry = true;
                arrow.traverse((o) => o.userData.ownsGeometry = true);
                this.overlay.add(arrow);
            }
            catch { }
        }
    }
    fit() { this.root.updateMatrixWorld(true); const box = new THREE.Box3().setFromObject(this.root); if (box.isEmpty())
        return; const center = box.getCenter(new THREE.Vector3()), size = box.getSize(new THREE.Vector3()), radius = Math.max(.05, size.length() / 2); this.controls.target.copy(center); this.camera.position.copy(center).add(new THREE.Vector3(1, -1.5, .9).normalize().multiplyScalar(radius * 3)); this.camera.near = Math.max(.0001, radius / 1000); this.camera.far = Math.max(10, radius * 100); this.camera.updateProjectionMatrix(); this.controls.update(); }
}
function receive(message) { if (message.type === 'state') {
    project = message.project;
    for (const l of project.Links) {
        l.Origin = l.Origin || originDraft();
        l.ShowFrame = !!l.ShowFrame;
    }
    properties = message.properties;
    originErrors = message.originErrors || {};
    assignedMassKg = message.assignedMassKg ?? project.Components.filter(c => !c.Excluded && project.Links.some(l => l.Components.includes(c.Id))).reduce((sum, c) => sum + (c.Override.Mode === 'complete' ? c.Override.Complete.Mass : c.Override.Mode === 'density' && c.UnitDensity ? c.UnitDensity.Mass * c.Override.Density : c.Cad.Mass), 0);
    meshUrls = message.meshUrls;
    busy = message.busy;
    render();
    return;
} if (message.type === 'progress') {
    toast(`Reading CAD ${message.n + 1} / ${message.count}: ${message.name}`);
    return;
} if (message.type === 'notice' || message.type === 'error') {
    toast(message.message, message.type === 'error' ? 'error' : 'success');
    return;
} if (message.type === 'validation') {
    $('diagnostics').innerHTML = message.diagnostics.length ? message.diagnostics.map(d => `<div class="diagnostic"><strong>${esc(d.Target)}</strong><small>${esc(d.Message)}</small></div>`).join('') : '<p class="success">All checks passed. The robot is ready to export.</p>';
    $('validation').showModal();
    return;
} if (message.type === 'selection') {
    cachedSelection = message.ids;
    if (selection.type === 'link')
        mutate(() => { const l = project.Links.find(l => l.Id === selection.id); for (const id of cachedSelection) {
            project.Links.forEach(other => other.Components = other.Components.filter(x => x !== id));
            l.Components.push(id);
            project.Components.find(c => c.Id === id).Excluded = false;
        } });
    toast(cachedSelection.length + ' CAD component(s) assigned.');
} }
document.querySelectorAll('[data-command]').forEach(button => button.onclick = () => { const type = button.dataset.command; if (type === 'capture' && project?.ZeroCaptured && !confirm('Recapture the current CAD pose as zero? Existing joint frames require review.'))
    return; if (type === 'new' && project?.Components.length && !confirm('Discard the current robot setup? Save it first if you need to keep it.'))
    return; send({ type }); });
bind('add-link', 'click', () => { if (!project?.ZeroCaptured)
    return toast('Capture CAD before creating links.', 'error'); mutate(() => { const l = newLink(); selection = { type: 'link', id: l.Id }; }); });
bind('search', 'input', () => { search = value('search').toLowerCase(); renderStructure(); });
bind('close-validation', 'click', () => $('validation').close());
bind('reset', 'click', () => { positions = {}; renderSliders(); viewer?.pose(); });
bind('fit', 'click', () => viewer?.fit());
bind('align-model', 'click', () => { select('project'); $('model-alignment')?.scrollIntoView({ block: 'start', behavior: 'smooth' }); });
bind('frames', 'change', () => { if (project)
    mutate(() => { const show = $('frames').checked; project.Links.forEach(l => l.ShowFrame = show); }); });
for (const id of ['cogs', 'ellipsoids'])
    bind(id, 'change', () => viewer?.pose());
$('inspector-title').onclick = () => select('project');
bind('robot-settings', 'click', () => { select('project'); $('robot-naming')?.scrollIntoView({ block: 'start' }); });
try {
    viewer = new RobotViewer();
}
catch (error) {
    $('viewer-error').hidden = false;
    $('viewer-error').textContent = '3D rendering unavailable: ' + error.message;
}
window.chrome?.webview?.addEventListener('message', (event) => receive(event.data));
// A read-only browser harness can inject fixture state for validation without SolidWorks.
window.__CAD_URDF_TEST__ = { receive, get project() { return project; }, get positions() { return positions; }, get viewer() { return viewer; }, select };
send({ type: 'ready' });
