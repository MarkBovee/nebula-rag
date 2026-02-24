import { spawn } from 'node:child_process';

/// <summary>
/// Parses CLI arguments in the format --key value and returns a lookup object.
/// </summary>
/// <param name="argv">Raw process arguments.</param>
/// <returns>Dictionary of parsed key-value pairs.</returns>
function parseArgs(argv) {
  const result = {};

  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith('--')) {
      continue;
    }

    const key = token.slice(2);
    const next = argv[index + 1];
    if (!next || next.startsWith('--')) {
      result[key] = 'true';
      continue;
    }

    result[key] = next;
    index += 1;
  }

  return result;
}

const args = parseArgs(process.argv.slice(2));
const homeAssistantOrigin = args.origin || process.env.HA_ORIGIN || 'http://homeassistant.local:8099';
const homeAssistantBasePath = args.basePath || process.env.HA_BASE_PATH || '/nebula';

const env = {
  ...process.env,
  VITE_DEV_PROXY_ORIGIN: homeAssistantOrigin,
  VITE_DEV_PROXY_BASE_PATH: homeAssistantBasePath,
};

console.log(`Starting dashboard dev server against Home Assistant API:`);
console.log(`  origin: ${homeAssistantOrigin}`);
console.log(`  basePath: ${homeAssistantBasePath}`);

const child = spawn('npm', ['run', 'dev'], {
  env,
  stdio: 'inherit',
  shell: true,
});

child.on('exit', (code) => {
  process.exit(code ?? 0);
});

child.on('error', (error) => {
  console.error('Failed to start dashboard dev server:', error);
  process.exit(1);
});
