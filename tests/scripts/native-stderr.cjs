console.error('warning-one')
console.error('warning-two')
process.exitCode = Number(process.argv[2] || 0)
