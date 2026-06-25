// Compute TOTP (RFC6238, SHA1, 30s, 6 digits) from a base32 secret.
// Usage: node totp.mjs <BASE32SECRET>
import crypto from 'node:crypto'
function b32decode(s) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  s = s.replace(/=+$/,'').toUpperCase().replace(/\s/g,'')
  let bits = ''
  for (const c of s) {
    const v = alphabet.indexOf(c)
    if (v < 0) continue
    bits += v.toString(2).padStart(5,'0')
  }
  const bytes = []
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i,i+8),2))
  return Buffer.from(bytes)
}
function totp(secret, t = Date.now()) {
  const key = b32decode(secret)
  let counter = Math.floor(t / 1000 / 30)
  const buf = Buffer.alloc(8)
  for (let i = 7; i >= 0; i--) { buf[i] = counter & 0xff; counter = Math.floor(counter / 256) }
  const hmac = crypto.createHmac('sha1', key).update(buf).digest()
  const offset = hmac[hmac.length - 1] & 0xf
  const code = ((hmac[offset] & 0x7f) << 24) | ((hmac[offset+1] & 0xff) << 16) | ((hmac[offset+2] & 0xff) << 8) | (hmac[offset+3] & 0xff)
  return (code % 1000000).toString().padStart(6,'0')
}
console.log(totp(process.argv[2]))
