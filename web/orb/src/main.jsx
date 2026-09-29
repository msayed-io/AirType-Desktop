import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ThinkingOrb } from 'thinking-orbs';
import './style.css';

const allowed = new Set(['working', 'breathing', 'listening', 'connecting', 'searching', 'solving', 'weaving', 'composing', 'shaping']);

function post(message) {
  if (window.chrome?.webview) window.chrome.webview.postMessage(message);
}

function App() {
  const [state, setState] = useState('breathing');
  useEffect(() => {
    window.setOrbState = (next) => setState(allowed.has(next) ? next : 'breathing');
    return () => { delete window.setOrbState; };
  }, []);

  return <main aria-label="AirType floating indicator" onClick={() => post({ type: 'click' })}>
    <ThinkingOrb state={state} size={64} speed={state === 'working' ? 1.08 : 0.76} dark />
  </main>;
}

createRoot(document.getElementById('root')).render(<App />);
