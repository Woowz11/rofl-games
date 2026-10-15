const express = require('express');
const http = require('http');
const { Server } = require('socket.io');
const axios = require('axios');
const fs = require('fs');
const path = require('path');
const { execSync, exec } = require('child_process');

const app = express();
const server = http.createServer(app);
const io = new Server(server);

const STATE_FILE = 'scan_state.json';
let isPaused = false;
let scanning = false;

app.use(express.static('public'));
app.use(express.json());

// Функция вызова окна через Base64 (чтобы кавычки не ломали команду)
function openSystemSaveDialog(title, filter) {
    const psScript = `
        Add-Type -AssemblyName System.Windows.Forms
        $f = New-Object System.Windows.Forms.SaveFileDialog
        $f.Title = "${title}"
        $f.Filter = "${filter}"
        $f.InitialDirectory = [System.IO.Directory]::GetCurrentDirectory()
        if($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            Write-Host $f.FileName
        }
    `;
    
    try {
        // Кодируем скрипт в Base64 для безопасной передачи в PowerShell
        const b64 = Buffer.from(psScript, 'utf16le').toString('base64');
        const result = execSync(`powershell -NoProfile -EncodedCommand ${b64}`, { encoding: 'utf-8' }).trim();
        return result;
    } catch (e) {
        console.error("PS Error:", e);
        throw new Error("Системная ошибка: Убедитесь, что у вас Windows и PowerShell доступен.");
    }
}

async function getLimits(token) {
    const headers = token ? { Authorization: `token ${token}` } : {};
    try {
        const res = await axios.get('https://api.github.com/rate_limit', { headers });
        return res.data.rate;
    } catch (e) { return null; }
}

io.on('connection', async (socket) => {
    // Загрузка токенов при входе
    socket.on('check-limits', async (token) => {
        const l = await getLimits(token);
        if(l) socket.emit('limits', l);
    });

    socket.on('browse-path', () => {
        try {
            const p = openSystemSaveDialog("Выберите файл для сохранения результата", "JSONL files (*.jsonl)|*.jsonl");
            if (p) socket.emit('path-selected', p);
        } catch (e) {
            socket.emit('log', { type: 'error', msg: e.message });
        }
    });

    socket.on('export-config', (cfg) => {
        try {
            const p = openSystemSaveDialog("Сохранить конфигурацию как...", "JSON files (*.json)|*.json");
            if (p) {
                fs.writeFileSync(p, JSON.stringify(cfg, null, 2));
                socket.emit('log', { type: 'success', msg: `Конфиг экспортирован: ${p}` });
            }
        } catch (e) {
            socket.emit('log', { type: 'error', msg: e.message });
        }
    });

    socket.on('start', (cfg) => {
        if (!scanning) startScanner(socket, cfg);
    });

    socket.on('pause', () => { isPaused = true; scanning = false; io.emit('scanning-state', false); });
    
    socket.on('reset', () => {
        if (fs.existsSync(STATE_FILE)) fs.unlinkSync(STATE_FILE);
        socket.emit('log', { type: 'info', msg: 'Прогресс сброшен.' });
    });
});

async function startScanner(socket, config) {
    let repoPath = config.repo.trim();
    if (repoPath.includes('github.com/')) {
        repoPath = repoPath.split('github.com/')[1].split('/').slice(0, 2).join('/');
    }

    if (!config.outputPath) {
        socket.emit('log', { type: 'error', msg: 'ОШИБКА: Не выбран путь сохранения!' });
        return;
    }

    scanning = true;
    isPaused = false;
    io.emit('scanning-state', true);

    const fullOutputPath = config.outputPath;
    const dir = path.dirname(fullOutputPath);
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });

    let state = (fs.existsSync(STATE_FILE)) ? JSON.parse(fs.readFileSync(STATE_FILE)) : {
        stack: [], processedFolders: [], fileCount: 0, folderCount: 0, targetRepo: repoPath
    };

    const headers = config.token ? { Authorization: `token ${config.token}` } : {};
    
    try {
        if (state.stack.length === 0) {
            const repoRes = await axios.get(`https://api.github.com/repos/${repoPath}`, { headers });
            const branchRes = await axios.get(`https://api.github.com/repos/${repoPath}/branches/${repoRes.data.default_branch}`, { headers });
            state.stack.push({ sha: branchRes.data.commit.commit.tree.sha, path: '' });
            fs.writeFileSync(fullOutputPath, ''); 
        }

        while (state.stack.length > 0 && !isPaused) {
            const current = state.stack.pop();
            try {
                const res = await axios.get(`https://api.github.com/repos/${repoPath}/git/trees/${current.sha}`, { headers });
                socket.emit('limits', { remaining: res.headers['x-ratelimit-remaining'], limit: res.headers['x-ratelimit-limit'], reset: res.headers['x-ratelimit-reset'] });

                for (const item of res.data.tree) {
                    const fullItemPath = current.path ? `${current.path}/${item.path}` : item.path;
                    if (item.type === 'tree') {
                        state.stack.push({ sha: item.sha, path: fullItemPath });
                        state.folderCount++;
                    } else {
                        state.fileCount++;
                        fs.appendFileSync(fullOutputPath, JSON.stringify({ p: fullItemPath, s: item.size }) + '\n');
                    }
                }
                fs.writeFileSync(STATE_FILE, JSON.stringify(state));
                socket.emit('status', { stats: state });
            } catch (err) {
                socket.emit('log', { type: 'error', msg: `GitHub API Error: ${err.message}` });
                isPaused = true;
            }
            await new Promise(r => setTimeout(r, 200));
        }
    } catch (e) {
        socket.emit('log', { type: 'error', msg: `Критический сбой: ${e.message}` });
    }
    
    scanning = false;
    io.emit('scanning-state', false);
}

const PORT = 3000;
server.listen(PORT, () => {
    exec(`start http://localhost:${PORT}`);
});