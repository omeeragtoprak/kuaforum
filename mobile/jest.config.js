/** @type {import('jest').Config} */
module.exports = {
  preset: 'jest-expo',
  // Şimdilik sadece "pure logic" testleri hedefleniyor (lib/, store/).
  // RNTL component/screen testleri ayrı bir iş — o zaman testMatch genişler.
  testPathIgnorePatterns: ['/node_modules/', '/android/', '/ios/', '/.expo/'],
};
