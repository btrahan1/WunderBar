const mofo = window.chrome.webview.hostObjects.mofo;

async function init() {
    console.log("Initializing MofoBar UI...");
    setupEventListeners();
    await refreshData();
    setInterval(refreshData, 5000); 
    document.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        
        const appItem = e.target.closest('.app-item');
        if (appItem) {
            const app = JSON.parse(appItem.dataset.raw);
            const isPinned = appItem.parentElement.id === 'pinned-apps';
            mofo.ShowAppMenu(app.Handle || 0, app.Title || app.Name, isPinned, app.ExePath || "", e.clientX, e.clientY);
        } else {
            mofo.ShowGlobalMenu(e.clientX, e.clientY);
        }
    });

    document.addEventListener('mousedown', (e) => {
        if (e.target.tagName === 'BODY' || e.target.classList.contains('divider')) {
            mofo.StartDrag();
        }
    });
}

function setupEventListeners() {
    document.getElementById('win-btn').addEventListener('click', async () => {
        console.log("Win Button clicked");
        await mofo.SimulateWinKey();
    });

    // Panels
    const searchBtn = document.getElementById('search-btn');
    const clipboardBtn = document.getElementById('clipboard-btn');
    const treeBtn = document.getElementById('tree-btn');

    searchBtn.addEventListener('click', (e) => {
        mofo.ShowFlyout('search', e.clientX, e.clientY);
    });
    clipboardBtn.addEventListener('click', (e) => {
        mofo.ShowFlyout('clipboard', e.clientX, e.clientY);
    });
    document.getElementById('tree-btn').onclick = (e) => {
        mofo.ShowFlyout('tree', e.clientX, e.clientY);
    };

    document.getElementById('tray-btn').onclick = (e) => {
        mofo.ShowFlyout('tray', e.clientX, e.clientY);
    };

    // Listen for backend messages (Clipboard updates)
    window.chrome.webview.addEventListener('message', event => {
        try {
            const msg = JSON.parse(event.data);
            if (msg.type === 'clipboard') {
                // Main bar doesn't need to render clipboard anymore, 
                // but could use it for a badge or something later.
            }
        } catch(e) {}
    });
}

window.setOrientation = function(side) {
    const container = document.getElementById('mofo-container');
    container.classList.remove('top', 'bottom', 'left', 'right');
    container.classList.add(side);
    
    // Trigger a refresh to re-calculate sizes
    refreshData();
};

async function refreshData() {
    try {
        console.log("Refreshing data...");
        const pinnedJson = await mofo.GetPinnedApps();
        const openJson = await mofo.GetOpenWindows();
        
        const pinned = JSON.parse(pinnedJson || "[]");
        const open = JSON.parse(openJson || "[]");
        
        console.log(`Fetched ${pinned.length} pinned and ${open.length} open apps`);

        renderPinnedApps(pinned);
        renderOpenApps(open);

        // Resize the host window to fit the content
        setTimeout(async () => {
            const container = document.getElementById('mofo-container');
            const isVertical = container.classList.contains('left') || container.classList.contains('right');
            
            let width, height;
            if (isVertical) {
                width = 140;
                height = Math.ceil(container.scrollHeight) + 100;
            } else {
                width = Math.ceil(container.scrollWidth) + 150;
                height = 140;
            }
            await mofo.ResizeWindow(width, height);
        }, 100);

    } catch (e) {
        console.error("Error fetching data:", e);
    }
}

function renderPinnedApps(apps) {
    const container = document.getElementById('pinned-apps');
    container.innerHTML = '';
    
    apps.forEach(app => {
        const div = document.createElement('div');
        div.className = 'app-item';
        div.setAttribute('data-name', app.Name);
        div.setAttribute('data-raw', JSON.stringify(app).replace(/'/g, "&apos;"));
        
        if (app.Icon) {
            div.innerHTML = `<img src="data:image/png;base64,${app.Icon}" class="app-icon" alt="${app.Name}">`;
        } else {
            div.innerHTML = `<div class="app-icon-placeholder">${app.Name[0]}</div>`;
        }
        
        div.addEventListener('click', () => mofo.LaunchApp(app.Path));
        container.appendChild(div);
    });
}

function renderOpenApps(apps) {
    const container = document.getElementById('open-apps');
    container.innerHTML = '';
    
    // De-duplicate by title for now
    const uniqueApps = [];
    const seen = new Set();
    
    apps.forEach(app => {
        if (!seen.has(app.Title)) {
            uniqueApps.push(app);
            seen.add(app.Title);
        }
    });

    uniqueApps.forEach(app => {
        const div = document.createElement('div');
        div.className = 'app-item';
        div.setAttribute('data-name', app.Title);
        div.setAttribute('data-raw', JSON.stringify(app).replace(/'/g, "&apos;"));
        
        if (app.Icon) {
            div.innerHTML = `<img src="data:image/png;base64,${app.Icon}" class="app-icon" alt="${app.Title}">`;
        } else {
            div.innerHTML = `<div class="app-icon-placeholder" style="background: linear-gradient(135deg, #2ecc71, #27ae60)">${app.Title[0]}</div>`;
        }
        
        div.addEventListener('click', () => mofo.FocusWindow(app.Handle));
        container.appendChild(div);
    });
}

// Vitals Charts Logic
const cpuHistory = new Array(20).fill(0);
const ramHistory = new Array(20).fill(0);

function updateCharts(vitals) {
    drawChart('cpu-chart', cpuHistory, vitals.Cpu, 'hsl(210, 100%, 60%)');
    drawChart('ram-chart', ramHistory, vitals.Ram, 'hsl(280, 100%, 60%)');
}

function drawChart(canvasId, history, newValue, color) {
    history.push(newValue);
    history.shift();

    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const w = canvas.width;
    const h = canvas.height;

    ctx.clearRect(0, 0, w, h);

    // Gradient fill
    const grad = ctx.createLinearGradient(0, 0, 0, h);
    grad.addColorStop(0, color.replace('100%', '30%').replace('60%', '20%'));
    grad.addColorStop(1, 'transparent');

    ctx.beginPath();
    ctx.moveTo(0, h);
    
    for (let i = 0; i < history.length; i++) {
        const x = (i / (history.length - 1)) * w;
        const y = h - (history[i] / 100) * h;
        ctx.lineTo(x, y);
    }

    ctx.lineTo(w, h);
    ctx.fillStyle = grad;
    ctx.fill();

    // Line
    ctx.beginPath();
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.lineJoin = 'round';

    for (let i = 0; i < history.length; i++) {
        const x = (i / (history.length - 1)) * w;
        const y = h - (history[i] / 100) * h;
        if (i === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
    }
    ctx.stroke();
}

async function refreshVitals() {
    try {
        const vitalsJson = await mofo.GetVitals();
        const vitals = JSON.parse(vitalsJson);
        updateCharts(vitals);
    } catch(e) {}
}

// Initial call
init();
setInterval(refreshVitals, 1000);
