const response = await fetch("http://localhost:5181/inbox", {
  method: "POST",
  headers: {
    Authorization: process.env.API_KEY,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({
    title: "This is test note 4",
    type: "note",
    source: "script",
    version: 1,
    content: {
      type: "html",
      data: "<p>This is test note content 3</p>",
    },
  }),
});
console.log(await response.text());
